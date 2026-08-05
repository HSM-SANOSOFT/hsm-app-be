using System.Collections.Concurrent;
using Hsm.Application.Abstractions;
using Hsm.Application.Ports;
using Hsm.Infrastructure.Queue;
using Hsm.Infrastructure.Persistence;
using Hsm.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace Hsm.Integration.Tests;

/// <summary>
/// The cluster half of "coms is strictly serial", against the real Redis.
///
/// <para><c>Consumers = 1</c> only ever meant one loop in one process: two
/// worker replicas would each run their one loop and take a job each, and the
/// frozen resend ordering — no newer job starting while an earlier one is still
/// working through its retries — would be gone the moment the worker was scaled
/// out. A serial queue's loop therefore runs only while its replica holds the
/// queue's lease. These tests are about who is allowed to consume, so they use
/// a dedicated serial queue and count which worker ran what.</para>
/// </summary>
public sealed class ConsumerLeaseTests : IAsyncLifetime
{
    /// <summary>Which worker ran each job, across every provider in the test process.</summary>
    private static readonly ConcurrentDictionary<Guid, string> RanBy = new();

    private string _prefix = null!;

    /// <summary>A job that records the worker that ran it.</summary>
    [JobName("lease.echo")]
    [AllowAnonymousRequest]
    public sealed record LeaseEchoCommand(Guid RunId) : ICommand<Unit>;

    public async Task InitializeAsync()
    {
        _prefix = $"hsmtest:{Guid.NewGuid():N}";

        // TransactionBehavior opens a real transaction per command, so the
        // schema has to exist even though this handler writes nothing.
        await using var provider = BuildWorker("schema", _prefix);
        await TestServices.MigrateAsync(provider);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Only_the_lease_holder_consumes_a_serial_queue_and_shutdown_hands_it_over()
    {
        await using var alpha = BuildWorker("alpha", _prefix);
        await using var beta = BuildWorker("beta", _prefix);

        await StartConsumerAsync(alpha);
        await StartConsumerAsync(beta);

        // Both workers are live on the same queue. Whichever took the lease
        // first is the only one that may consume, so every job runs on ONE of
        // them — which is the guarantee, not which one wins.
        var first = await RunJobAsync(alpha);
        var second = await RunJobAsync(alpha);
        var holder = RanBy[first];
        Assert.Equal(holder, RanBy[second]);

        // The holder shuts down cleanly and releases the lease rather than
        // making its sibling wait out the TTL. The other worker takes over.
        var stopped = holder == "alpha" ? alpha : beta;
        var survivor = holder == "alpha" ? "beta" : "alpha";
        await StopConsumerAsync(stopped);

        var third = await RunJobAsync(alpha);
        Assert.Equal(survivor, RanBy[third]);

        await StopConsumerAsync(holder == "alpha" ? beta : alpha);
    }

    [Fact]
    public async Task A_lease_held_by_a_dead_worker_blocks_consumption_until_it_expires()
    {
        await using var worker = BuildWorker("alpha", _prefix);
        var topology = worker.GetRequiredService<JobQueueTopology>();
        var db = await worker.GetRequiredService<IJobConnection>().GetDatabaseAsync();

        // A worker that died holding the lease: the key is there, nobody is
        // renewing it, and it expires on its own. That expiry IS the failover
        // window — the price of using a lease instead of pinning consumption to
        // one deployed instance.
        Assert.True(await db.StringSetAsync(
            topology.LeaseKey("lease"), "a-worker-that-died", topology.LeaseTtl, When.NotExists));

        var runId = Guid.NewGuid();
        await EnqueueAsync(worker, new LeaseEchoCommand(runId));
        await StartConsumerAsync(worker);
        try
        {
            // Nothing runs while the dead worker's lease stands, even though
            // this worker is live and the job is sitting right there.
            await Task.Delay(300);
            Assert.False(RanBy.ContainsKey(runId));

            // Once it lapses, the live worker takes over and drains it.
            await WaitForAsync(() => RanBy.ContainsKey(runId), "the surviving worker to take the lease");
            Assert.Equal("alpha", RanBy[runId]);
        }
        finally
        {
            await StopConsumerAsync(worker);
        }
    }

    [Fact]
    public async Task A_serial_queue_whose_lease_cannot_outlive_its_jobs_refuses_to_start()
    {
        // The configuration that would quietly break everything above: a lease
        // shorter than the queue's own statement of how long a job may take.
        // An ordinary slow job would lose it mid-flight and a sibling would
        // start consuming alongside — so the host does not start at all.
        await using var provider = BuildWorker(
            "alpha",
            _prefix,
            leaseTtl: TimeSpan.FromMilliseconds(200),
            claimMinIdle: TimeSpan.FromSeconds(30));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Consumer(provider).StartAsync(CancellationToken.None));

        Assert.Contains("consume lease", failure.Message, StringComparison.Ordinal);
        Assert.Contains("Queue:LeaseTtlMs", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Enqueues a job and waits for whichever worker holds the lease to run it.</summary>
    private static async Task<Guid> RunJobAsync(ServiceProvider enqueuer)
    {
        var runId = Guid.NewGuid();
        await EnqueueAsync(enqueuer, new LeaseEchoCommand(runId));
        await WaitForAsync(() => RanBy.ContainsKey(runId), $"job {runId} to run");
        return runId;
    }

    private static async Task EnqueueAsync(ServiceProvider provider, LeaseEchoCommand command)
    {
        using var scope = provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IJobQueue>().EnqueueAsync(command);
    }

    private static Task StartConsumerAsync(ServiceProvider provider) =>
        Consumer(provider).StartAsync(CancellationToken.None);

    private static Task StopConsumerAsync(ServiceProvider provider) =>
        Consumer(provider).StopAsync(CancellationToken.None);

    private static JobConsumerService Consumer(ServiceProvider provider) =>
        provider.GetServices<IHostedService>().OfType<JobConsumerService>().Single();

    private static async Task WaitForAsync(Func<bool> done, string what)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (done())
            {
                return;
            }

            await Task.Delay(25);
        }

        Assert.Fail($"Timed out waiting for {what}.");
    }

    /// <summary>
    /// One worker's composition on a dedicated serial queue, with a lease TTL
    /// short enough to expire inside a test.
    /// </summary>
    private static ServiceProvider BuildWorker(
        string workerName,
        string keyPrefix,
        TimeSpan? leaseTtl = null,
        TimeSpan? claimMinIdle = null) =>
        TestServices.Build(customizeServices: services =>
        {
            services.AddHsmPipeline();
            services.AddScoped<ICurrentPrincipal>(sp => sp.GetRequiredService<AmbientPrincipal>());
            services.AddHsmJobProcessing();
            services.AddSingleton(new WorkerName(workerName));
            services.AddScoped<IRequestHandler<LeaseEchoCommand, Unit>, LeaseEchoHandler>();
            services.AddSingleton(JobNameRegistry.Scan(
                typeof(IJobQueue).Assembly, typeof(ConsumerLeaseTests).Assembly));
            services.AddSingleton(new JobQueueTopology
            {
                KeyPrefix = keyPrefix,
                ConsumerName = $"{workerName}-{Guid.NewGuid():N}",
                LeaseTtl = leaseTtl ?? TimeSpan.FromMilliseconds(1500),
                Queues =
                [
                    new JobQueueDefinition(
                        "lease",
                        MaxAttempts: 3,
                        InitialDelay: TimeSpan.Zero,
                        RetryBaseDelay: TimeSpan.FromMilliseconds(10),
                        // Serial: this is the queue shape the lease exists for.
                        Consumers: 1,
                        // Under the lease TTL, as the worker's startup check
                        // requires: the lease has to outlive one job.
                        ClaimMinIdle: claimMinIdle ?? TimeSpan.FromMilliseconds(500)),
                ],
            });
        });

    private sealed record WorkerName(string Value);

    private sealed class LeaseEchoHandler(WorkerName worker) : IRequestHandler<LeaseEchoCommand, Unit>
    {
        public Task<Unit> HandleAsync(LeaseEchoCommand request, CancellationToken ct)
        {
            RanBy[request.RunId] = worker.Value;
            return Task.FromResult(Unit.Value);
        }
    }
}
