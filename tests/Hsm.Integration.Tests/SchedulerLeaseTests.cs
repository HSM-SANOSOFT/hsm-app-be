using System.Collections.Concurrent;
using Hsm.Application.Abstractions;
using Hsm.Infrastructure.Queue;
using Hsm.Worker;
using Hsm.Worker.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hsm.Integration.Tests;

/// <summary>
/// The <see cref="Scheduler"/> half of "a schedule fires once across the whole
/// worker fleet", against the real Redis. <see cref="Scheduler"/>'s own doc
/// comment describes the mechanism: every replica's <c>PeriodicTimer</c> ticks,
/// but only the one whose <c>SET {prefix}:sched:{name} {instanceId} NX PX
/// {interval * 0.9}</c> succeeds may dispatch. This test proves that property
/// against a real Redis rather than trusting the doc comment: two
/// <see cref="Scheduler"/> instances share one schedule name, and across
/// several ticks only one of them ever runs any given tick.
///
/// <see cref="Scheduler"/> is driven directly (not via <c>IHostedService</c>
/// host startup) because a <c>PeriodicTimer</c> interval is the one thing this
/// design cannot be handed a fake clock for — the honest test runs real wall
/// clock time over a few short intervals rather than pretending to control it,
/// with generous tolerances so ordinary CI jitter cannot flip it.
/// </summary>
public sealed class SchedulerLeaseTests : IAsyncLifetime
{
    private string _prefix = null!;

    [AllowAnonymousRequest]
    private sealed record ScheduleTickCommand(string InstanceId) : ICommand<Unit>;

    public async Task InitializeAsync()
    {
        _prefix = $"hsmtest:{Guid.NewGuid():N}";

        // TransactionBehavior opens a real transaction per dispatch, so the
        // schema has to exist even though this handler writes nothing.
        await using var provider = BuildScheduler("schema", _prefix, TimeSpan.FromMinutes(10), new());
        await TestServices.MigrateAsync(provider);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Only_one_scheduler_fires_each_tick_and_the_lease_key_names_the_winner()
    {
        var interval = TimeSpan.FromMilliseconds(300);
        var executions = new ConcurrentQueue<(string Instance, DateTimeOffset At)>();
        const string alphaName = "alpha";
        const string betaName = "beta";

        await using var alpha = BuildScheduler(alphaName, _prefix, interval, executions);
        await using var beta = BuildScheduler(betaName, _prefix, interval, executions);

        var schedulerAlpha = alpha.GetRequiredService<Scheduler>();
        var schedulerBeta = beta.GetRequiredService<Scheduler>();
        var topology = alpha.GetRequiredService<JobQueueTopology>();

        await schedulerAlpha.StartAsync(CancellationToken.None);
        await schedulerBeta.StartAsync(CancellationToken.None);
        try
        {
            // Comfortably past the first tick (nominal at ~300ms) but well
            // before the second (~600ms) and before the winner's own lease key
            // expires (interval * 0.9 = 270ms after it was set) — a window wide
            // enough to absorb ordinary scheduling jitter.
            await Task.Delay(interval + TimeSpan.FromMilliseconds(150));

            var db = await alpha.GetRequiredService<IJobConnection>().GetDatabaseAsync();
            var leaseValue = await db.StringGetAsync(topology.ScheduleKey("test.tick"));
            Assert.True(leaseValue.HasValue, "the first tick's winner should still hold its lease key");
            Assert.Contains(leaseValue.ToString(), new[] { alphaName, betaName });

            // Run a few more windows so the "never twice" property is checked
            // across several ticks, not just the first.
            await Task.Delay(interval * 3);
        }
        finally
        {
            await schedulerAlpha.StopAsync(CancellationToken.None);
            await schedulerBeta.StopAsync(CancellationToken.None);
        }

        var runs = executions.OrderBy(e => e.At).ToList();
        var windows = (int)((interval * 4).TotalMilliseconds / interval.TotalMilliseconds);

        // At-most-once-per-tick, not exactly-once: a tick neither replica is up
        // for is simply skipped (see ScheduleRegistry's doc comment), so the
        // count is bounded above, not pinned exactly.
        Assert.InRange(runs.Count, 1, windows + 1);
        Assert.All(runs, r => Assert.Contains(r.Instance, new[] { alphaName, betaName }));

        // The direct proof that no tick ever ran twice: two winners racing the
        // same SET NX would both dispatch within milliseconds of each other,
        // nowhere near a whole interval apart. Consecutive recorded runs are
        // never that close.
        for (var i = 1; i < runs.Count; i++)
        {
            var gap = runs[i].At - runs[i - 1].At;
            Assert.True(
                gap >= TimeSpan.FromMilliseconds(interval.TotalMilliseconds * 0.5),
                $"two runs landed only {gap.TotalMilliseconds}ms apart — the lease let both through");
        }
    }

    private static ServiceProvider BuildScheduler(
        string instanceName,
        string keyPrefix,
        TimeSpan interval,
        ConcurrentQueue<(string Instance, DateTimeOffset At)> executions) =>
        TestServices.Build(customizeServices: services =>
        {
            services.AddHsmPipeline();
            services.TryAddScoped<AmbientPrincipal>();
            services.AddScoped<ICurrentPrincipal>(sp => sp.GetRequiredService<AmbientPrincipal>());
            services.AddScoped<IRequestHandler<ScheduleTickCommand, Unit>>(_ => new ScheduleTickHandler(executions));
            services.AddSingleton(new JobQueueTopology { KeyPrefix = keyPrefix, ConsumerName = instanceName });

            var registry = new ScheduleRegistry();
            registry.Add(new ScheduledJob(
                "test.tick", interval, (dispatcher, ct) => dispatcher.Send(new ScheduleTickCommand(instanceName), ct)));
            services.AddSingleton(registry);
            services.AddSingleton<Scheduler>();
        });

    private sealed class ScheduleTickHandler(ConcurrentQueue<(string, DateTimeOffset)> executions)
        : IRequestHandler<ScheduleTickCommand, Unit>
    {
        public Task<Unit> HandleAsync(ScheduleTickCommand request, CancellationToken ct)
        {
            executions.Enqueue((request.InstanceId, DateTimeOffset.UtcNow));
            return Task.FromResult(Unit.Value);
        }
    }
}
