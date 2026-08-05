using System.Collections.Concurrent;
using System.Text.Json;

using Hsm.Application.Abstractions;
using Hsm.Application.Ports;
using Hsm.Infrastructure.Queue;
using Hsm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Hsm.Integration.Tests;

/// <summary>
/// The defect this queue exists to fix, proven against the real Redis in the
/// dev container: the in-memory channel queue lost every queued job when its
/// process went away, and with N host instances each held its own invisible
/// queue. Redis Streams with a consumer group give at-least-once delivery
/// across processes, a delayed set for backoff, and a dead-letter stream.
///
/// The tests drive <see cref="IJobConsumer.DrainOnceAsync"/> directly rather
/// than the hosted consume loop — the loop is a <c>while</c> around this call,
/// and driving it directly makes the assertions deterministic instead of
/// timing-dependent.
/// </summary>
public sealed class JobQueueDurabilityTests : IAsyncLifetime
{
    /// <summary>Attempts observed per run id, across every provider in the test process.</summary>
    private static readonly ConcurrentDictionary<Guid, int> Attempts = new();

    private string _prefix = null!;

    /// <summary>A job that records that it ran. Anonymous: it proves delivery, not policy.</summary>
    [JobName("test.echo")]
    [AllowAnonymousRequest]
    public sealed record EchoJobCommand(Guid RunId) : ICommand<Unit>;

    /// <summary>A job that fails every attempt, so retries exhaust into the dead-letter stream.</summary>
    [JobName("test.always-fails")]
    [AllowAnonymousRequest]
    public sealed record AlwaysFailsJobCommand(Guid RunId) : ICommand<Unit>;

    /// <summary>A job only an admin may run — the queued counterpart of an admin-only route.</summary>
    [JobName("test.admin-only")]
    [RequireRole("admin")]
    public sealed record AdminOnlyJobCommand(Guid RunId) : ICommand<Unit>;

    /// <summary>
    /// A job the "old" worker in the rolling-deploy test has never heard of,
    /// and the "new" one has: which of the two a provider is depends on whether
    /// this assembly is in its registry's scan set.
    /// </summary>
    [JobName("test.deployed-later")]
    [AllowAnonymousRequest]
    public sealed record DeployedLaterCommand(Guid RunId) : ICommand<Unit>;

    public async Task InitializeAsync()
    {
        _prefix = $"hsmtest:{Guid.NewGuid():N}";

        // TransactionBehavior opens a real transaction for every ICommand, so
        // the schema has to exist even though these handlers write nothing.
        await using var provider = BuildProvider(_prefix);
        await TestServices.MigrateAsync(provider);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Job_enqueued_before_a_consumer_restart_still_runs()
    {
        var runId = Guid.NewGuid();

        // Provider A enqueues and is disposed WITHOUT ever consuming. This is
        // the restart: the channel queue lost the job at exactly this point.
        await using (var producer = BuildProvider(_prefix))
        {
            await EnqueueAsync(producer, new EchoJobCommand(runId));
        }

        await using var consumer = BuildProvider(_prefix);
        var drained = await DrainUntilAsync(consumer, () => Ran(runId) == 1);

        Assert.Equal(1, drained);
        Assert.Equal(1, Ran(runId));
    }

    [Fact]
    public async Task Two_consumers_do_not_both_process_one_job()
    {
        var runId = Guid.NewGuid();
        await using var first = BuildProvider(_prefix);
        await using var second = BuildProvider(_prefix);

        await EnqueueAsync(first, new EchoJobCommand(runId));

        // Two consumer NAMES in one group (hsm-workers): Redis hands the entry
        // to exactly one of them, so the handler runs once, not twice.
        var counts = await Task.WhenAll(
            first.GetRequiredService<IJobConsumer>().DrainOnceAsync("test", 10, CancellationToken.None),
            second.GetRequiredService<IJobConsumer>().DrainOnceAsync("test", 10, CancellationToken.None));

        Assert.Equal(1, counts.Sum());
        Assert.Equal(1, Ran(runId));
    }

    [Fact]
    public async Task Job_failing_every_attempt_lands_in_the_dead_letter_stream()
    {
        var runId = Guid.NewGuid();
        await using var provider = BuildProvider(_prefix);

        await EnqueueAsync(provider, new AlwaysFailsJobCommand(runId));
        await DrainUntilAsync(provider, () => Ran(runId) >= TestAttempts);

        // Every attempt was spent, and the envelope is in the dead-letter
        // stream — not lost, not still pending on the live one.
        Assert.Equal(TestAttempts, Ran(runId));
        var dead = await ReadDeadLetterAsync(provider, runId);
        Assert.Equal("test.always-fails", dead.JobName);
        Assert.Equal(TestAttempts, dead.Attempt);
        Assert.Equal(0, await UnsettledAsync(provider));
        Assert.Equal(0, await DelayedCountAsync(provider));
    }

    [Fact]
    public async Task Queued_command_authorizes_with_the_enqueuing_actor()
    {
        var allowed = Guid.NewGuid();
        var refused = Guid.NewGuid();
        await using var provider = BuildProvider(_prefix);

        // Enqueued by an admin: the envelope carries that actor, the consumer
        // installs it, and AuthorizationBehavior lets the command through.
        await EnqueueAsync(
            provider, new AdminOnlyJobCommand(allowed), Actor("admin"));
        await DrainUntilAsync(provider, () => Ran(allowed) == 1);
        Assert.Equal(1, Ran(allowed));

        // Enqueued by a doctor: the same command, refused by the same policy
        // that refuses it over HTTP, until its attempts are spent.
        await EnqueueAsync(
            provider, new AdminOnlyJobCommand(refused), Actor("doctor"));
        await DrainUntilAsync(provider, async () => await DeadLetterCountAsync(provider) > 0);

        Assert.Equal(0, Ran(refused));
        var dead = await ReadDeadLetterAsync(provider, refused);
        Assert.Equal("test.admin-only", dead.JobName);
    }

    [Fact]
    public async Task Job_naming_a_type_this_worker_does_not_have_outlives_a_deploy_and_then_runs()
    {
        var runId = Guid.NewGuid();

        // The worker on the OLD image: its registry has never heard of the job
        // name, exactly like a pod that has not been rolled yet.
        await using var oldWorker = BuildProvider(_prefix, knowsDeployedLater: false);
        var topology = oldWorker.GetRequiredService<JobQueueTopology>();
        var db = await oldWorker.GetRequiredService<IJobConnection>().GetDatabaseAsync();
        await db.StreamAddAsync(
            topology.StreamKey("test"),
            JobEnvelope.Field,
            new JobEnvelope(
                "test.deployed-later",
                JsonSerializer.Serialize(new { runId }),
                null,
                Attempt: 1).ToJson());

        // Hand it back FAR more times than this queue's ordinary budget would
        // have allowed (MaxAttempts is 3): the unknown-name wait is a budget of
        // its own, because what it has to outlast is a deploy, not a flaky
        // dependency. Nothing is dead-lettered and nothing is lost.
        var consumer = oldWorker.GetRequiredService<IJobConsumer>();
        for (var i = 0; i < (TestAttempts * 3) + 1; i++)
        {
            Assert.Equal(1, await consumer.DrainOnceAsync("test", 10, CancellationToken.None));
            await Task.Delay(15);
        }

        Assert.Equal(0, await DeadLetterCountAsync(oldWorker));
        Assert.Equal(0, Ran(runId));

        // The deploy lands. A worker that knows the name picks the same job up
        // and runs it — with its real attempt budget never having been touched.
        await using var newWorker = BuildProvider(_prefix, knowsDeployedLater: true);
        await DrainUntilAsync(newWorker, () => Ran(runId) == 1);

        Assert.Equal(1, Ran(runId));
        Assert.Equal(0, await DeadLetterCountAsync(newWorker));
        Assert.Equal(0, await UnsettledAsync(newWorker));
    }

    [Fact]
    public async Task Job_naming_a_type_this_worker_does_not_have_is_rescheduled_not_dead_lettered()
    {
        await using var provider = BuildProvider(_prefix);
        var topology = provider.GetRequiredService<JobQueueTopology>();
        var db = await provider.GetRequiredService<IJobConnection>().GetDatabaseAsync();

        // The shape of a rolling deploy: a producer that has already been
        // updated enqueues a job whose handler only exists in the new worker
        // image, and an old worker picks it up first.
        await db.StreamAddAsync(
            topology.StreamKey("test"),
            JobEnvelope.Field,
            new JobEnvelope("test.not-yet-deployed", "{}", null, Attempt: 1).ToJson());

        var processed = await provider.GetRequiredService<IJobConsumer>()
            .DrainOnceAsync("test", 10, CancellationToken.None);

        // Settled, but as a retry: dead-lettering here would destroy real work
        // for the length of every deploy. It waits on the delayed set for a
        // worker that knows the name.
        Assert.Equal(1, processed);
        Assert.Equal(0, await DeadLetterCountAsync(provider));
        Assert.Equal(1, await DelayedCountAsync(provider));
        Assert.Equal(0, await UnsettledAsync(provider));
    }

    [Fact]
    public async Task Job_left_pending_by_a_dead_consumer_is_reclaimed_after_the_idle_threshold()
    {
        var runId = Guid.NewGuid();
        await using var provider = BuildProvider(_prefix, claimMinIdle: TimeSpan.FromMilliseconds(50));

        await EnqueueAsync(provider, new EchoJobCommand(runId));
        await AbandonAsync(provider, "a-consumer-that-died");

        // Nobody will ever XACK that entry. Once it has been idle longer than
        // the queue's threshold, XAUTOCLAIM hands it to a live consumer — which
        // is the whole recovery story for a worker that is killed mid-job.
        await Task.Delay(120);
        var drained = await DrainUntilAsync(provider, () => Ran(runId) == 1);

        Assert.Equal(1, drained);
        Assert.Equal(1, Ran(runId));
        Assert.Equal(0, await UnsettledAsync(provider));
    }

    [Fact]
    public async Task Job_still_being_worked_on_is_not_reclaimed_before_the_idle_threshold()
    {
        var runId = Guid.NewGuid();
        await using var provider = BuildProvider(_prefix, claimMinIdle: TimeSpan.FromMinutes(10));

        await EnqueueAsync(provider, new EchoJobCommand(runId));
        await AbandonAsync(provider, "a-consumer-still-working");

        // The negative twin, and the reason the threshold is per-queue:
        // XAUTOCLAIM cannot tell a dead consumer from a slow one, so a job that
        // is merely taking a while must NOT be handed to a second consumer —
        // that is a duplicate run of a job that is still in flight.
        var consumer = provider.GetRequiredService<IJobConsumer>();
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(0, await consumer.DrainOnceAsync("test", 10, CancellationToken.None));
        }

        Assert.Equal(0, Ran(runId));
        Assert.Equal(1, await UnsettledAsync(provider));
    }

    /// <summary>Attempts the test topology allows each queued job.</summary>
    private const int TestAttempts = 3;

    /// <summary>
    /// Delivers the queue's entries to <paramref name="consumerName"/> and never
    /// acknowledges them — a consumer that took work and died holding it.
    /// </summary>
    private static async Task AbandonAsync(ServiceProvider provider, string consumerName)
    {
        var topology = provider.GetRequiredService<JobQueueTopology>();
        var db = await provider.GetRequiredService<IJobConnection>().GetDatabaseAsync();
        try
        {
            await db.StreamCreateConsumerGroupAsync(
                topology.StreamKey("test"),
                JobQueueTopology.ConsumerGroup,
                StreamPosition.Beginning,
                createStream: true);
        }
        catch (RedisServerException)
        {
            // BUSYGROUP: a drain in this test already created it.
        }

        var taken = await db.StreamReadGroupAsync(
            topology.StreamKey("test"),
            JobQueueTopology.ConsumerGroup,
            consumerName,
            StreamPosition.NewMessages,
            count: 10);
        Assert.NotEmpty(taken);
    }

    private static RequestActor Actor(string role) =>
        new(Guid.NewGuid().ToString(), [role], OnboardingCompleted: true);

    private static int Ran(Guid runId) => Attempts.GetValueOrDefault(runId);

    private static async Task EnqueueAsync<TCommand>(
        ServiceProvider provider, TCommand command, RequestActor? actor = null)
        where TCommand : ICommand<Unit>
    {
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<AmbientPrincipal>().Set(actor);
        await scope.ServiceProvider.GetRequiredService<IJobQueue>().EnqueueAsync(command);
    }

    /// <summary>Drains until <paramref name="done"/> holds; returns the jobs processed.</summary>
    private static Task<int> DrainUntilAsync(ServiceProvider provider, Func<bool> done) =>
        DrainUntilAsync(provider, () => Task.FromResult(done()));

    private static async Task<int> DrainUntilAsync(ServiceProvider provider, Func<Task<bool>> done)
    {
        var consumer = provider.GetRequiredService<IJobConsumer>();
        var processed = 0;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline)
        {
            processed += await consumer.DrainOnceAsync("test", 10, CancellationToken.None);
            if (await done())
            {
                return processed;
            }

            await Task.Delay(20);
        }

        Assert.Fail("Timed out draining the test queue.");
        return processed;
    }

    private static async Task<JobEnvelope> ReadDeadLetterAsync(ServiceProvider provider, Guid runId)
    {
        var topology = provider.GetRequiredService<JobQueueTopology>();
        var db = await provider.GetRequiredService<IJobConnection>().GetDatabaseAsync();
        var entries = await db.StreamRangeAsync(topology.DeadLetterKey("test"));
        var envelopes = entries
            .Select(e => JobEnvelope.FromJson(e[JobEnvelope.Field]!))
            .Where(e => e.Payload.Contains(runId.ToString(), StringComparison.OrdinalIgnoreCase))
            .ToList();
        return Assert.Single(envelopes);
    }

    private static async Task<long> DeadLetterCountAsync(ServiceProvider provider)
    {
        var topology = provider.GetRequiredService<JobQueueTopology>();
        var db = await provider.GetRequiredService<IJobConnection>().GetDatabaseAsync();
        return await db.StreamLengthAsync(topology.DeadLetterKey("test"));
    }

    private static async Task<long> DelayedCountAsync(ServiceProvider provider)
    {
        var topology = provider.GetRequiredService<JobQueueTopology>();
        var db = await provider.GetRequiredService<IJobConnection>().GetDatabaseAsync();
        return await db.SortedSetLengthAsync(topology.DelayedKey("test"));
    }

    /// <summary>Entries either stream's group handed out and never got an XACK for.</summary>
    private static async Task<long> UnsettledAsync(ServiceProvider provider)
    {
        var topology = provider.GetRequiredService<JobQueueTopology>();
        var db = await provider.GetRequiredService<IJobConnection>().GetDatabaseAsync();
        var live = await db.StreamPendingAsync(topology.StreamKey("test"), JobQueueTopology.ConsumerGroup);
        var due = await db.StreamPendingAsync(topology.DueKey("test"), JobQueueTopology.ConsumerGroup);
        return live.PendingMessageCount + due.PendingMessageCount;
    }

    private static ServiceProvider BuildProvider(
        string keyPrefix, TimeSpan? claimMinIdle = null, bool knowsDeployedLater = true) =>
        TestServices.Build(customizeServices: services =>
        {
            services.AddHsmPipeline();
            services.AddScoped<AmbientPrincipal>();
            services.AddScoped<ICurrentPrincipal>(sp => sp.GetRequiredService<AmbientPrincipal>());
            services.AddScoped<IRequestHandler<EchoJobCommand, Unit>, EchoJobHandler>();
            services.AddScoped<IRequestHandler<AlwaysFailsJobCommand, Unit>, AlwaysFailsJobHandler>();
            services.AddScoped<IRequestHandler<AdminOnlyJobCommand, Unit>, AdminOnlyJobHandler>();
            services.AddScoped<IRequestHandler<DeployedLaterCommand, Unit>, DeployedLaterHandler>();

            // The registry scans for [JobName]; this assembly's test commands
            // have to be in the scan set alongside Hsm.Application's real ones.
            // Leaving this assembly OUT is how a worker on the old image is
            // simulated: a build that has never heard of 'test.deployed-later'.
            services.AddSingleton(knowsDeployedLater
                ? JobNameRegistry.Scan(typeof(IJobQueue).Assembly, typeof(JobQueueDurabilityTests).Assembly)
                : JobNameRegistry.Scan(typeof(IJobQueue).Assembly));

            // A dedicated 'test' queue on a key prefix nothing else uses, with
            // a backoff short enough to exhaust inside a test.
            services.AddSingleton(new JobQueueTopology
            {
                KeyPrefix = keyPrefix,
                ConsumerName = $"consumer-{Guid.NewGuid():N}",
                UnknownJobRetryDelay = TimeSpan.FromMilliseconds(10),
                Queues =
                [
                    new JobQueueDefinition(
                        "test",
                        MaxAttempts: TestAttempts,
                        InitialDelay: TimeSpan.Zero,
                        RetryBaseDelay: TimeSpan.FromMilliseconds(10),
                        Consumers: 1,
                        ClaimMinIdle: claimMinIdle ?? TimeSpan.FromSeconds(30)),
                ],
            });
        });

    private sealed class EchoJobHandler : IRequestHandler<EchoJobCommand, Unit>
    {
        public Task<Unit> HandleAsync(EchoJobCommand request, CancellationToken ct)
        {
            Attempts.AddOrUpdate(request.RunId, 1, (_, count) => count + 1);
            return Task.FromResult(Unit.Value);
        }
    }

    private sealed class AlwaysFailsJobHandler : IRequestHandler<AlwaysFailsJobCommand, Unit>
    {
        public Task<Unit> HandleAsync(AlwaysFailsJobCommand request, CancellationToken ct)
        {
            Attempts.AddOrUpdate(request.RunId, 1, (_, count) => count + 1);
            throw new InvalidOperationException("Induced job failure.");
        }
    }

    private sealed class AdminOnlyJobHandler : IRequestHandler<AdminOnlyJobCommand, Unit>
    {
        public Task<Unit> HandleAsync(AdminOnlyJobCommand request, CancellationToken ct)
        {
            Attempts.AddOrUpdate(request.RunId, 1, (_, count) => count + 1);
            return Task.FromResult(Unit.Value);
        }
    }

    private sealed class DeployedLaterHandler : IRequestHandler<DeployedLaterCommand, Unit>
    {
        public Task<Unit> HandleAsync(DeployedLaterCommand request, CancellationToken ct)
        {
            Attempts.AddOrUpdate(request.RunId, 1, (_, count) => count + 1);
            return Task.FromResult(Unit.Value);
        }
    }
}
