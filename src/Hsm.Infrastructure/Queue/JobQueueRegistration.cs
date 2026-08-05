using Hsm.Application.Ports;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hsm.Infrastructure.Queue;

/// <summary>
/// Composition for the job queue. Every host registers this; only
/// <c>Hsm.Worker</c> additionally runs the loops that consume it
/// (<c>AddHsmJobProcessing</c>, in that host).
/// </summary>
public static class JobQueueRegistration
{
    /// <summary>
    /// The producing side plus the consumer implementation. Registered by every
    /// host; costs nothing until something enqueues, because
    /// <see cref="RedisJobConnection"/> connects on first use. Registering the
    /// <c>IJobConsumer</c> here does not make a host consume — a host consumes
    /// when something drives <c>DrainOnceAsync</c>, which only the worker's
    /// hosted service does.
    /// </summary>
    public static IServiceCollection AddHsmJobQueue(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Scanned once at startup: reflection over Hsm.Application, no I/O.
        services.AddSingleton(JobNameRegistry.Scan(typeof(IJobQueue).Assembly));
        services.AddSingleton(BuildTopology(configuration));
        services.AddSingleton<IJobConnection>(_ => new RedisJobConnection(
            configuration["Redis:ConnectionString"] ?? "localhost:6379"));
        services.AddScoped<IJobQueue, RedisStreamJobQueue>();
        services.AddSingleton<IJobConsumer, RedisStreamJobConsumer>();
        return services;
    }

    /// <summary>
    /// The per-queue posture: coms 5 attempts / 5s base / strictly serial;
    /// docs 3 attempts / 1s first-attempt delay / 2s base / four consumers.
    /// Every value is deployment-tunable
    /// because the contract suites tighten the timings.
    ///
    /// <para><c>Queue:KeyPrefix</c> is ONE namespace for the whole deployment.
    /// It is not derived from the host, the process or the boot: producers and
    /// the worker have to be looking at the same keys or the worker cannot see
    /// what the API enqueued. Test hosts that need isolation set the key
    /// through configuration like any other deployment would.</para>
    /// </summary>
    private static JobQueueTopology BuildTopology(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return new JobQueueTopology
        {
            KeyPrefix = configuration["Queue:KeyPrefix"] ?? "hsm",
            // The failover window for a serial queue: how long after a worker
            // dies holding the consume lease before another may take it. It has
            // to outlive one coms job (Coms:ClaimMinIdleMs, 30s), or an
            // ordinary slow send would lose the lease — the worker refuses to
            // start if it does not.
            LeaseTtl = Milliseconds(configuration, "Queue:LeaseTtlMs", 45_000),
            // A job name this build does not know is a rolling deploy, not a
            // failure: it waits on its own budget rather than burning the
            // queue's five-or-three attempts in seconds.
            UnknownJobRetryDelay = Milliseconds(configuration, "Queue:UnknownJobRetryDelayMs", 60_000),
            UnknownJobMaxAttempts = configuration.GetValue(
                "Queue:UnknownJobMaxAttempts", defaultValue: 30),
            PollInterval = Milliseconds(configuration, "Queue:PollIntervalMs", 100),
            DelayedPumpInterval = Milliseconds(configuration, "Queue:DelayedPumpIntervalMs", 100),
            StreamMaxLength = configuration.GetValue("Queue:StreamMaxLength", defaultValue: 100_000),
            Queues =
            [
                new JobQueueDefinition(
                    "coms",
                    MaxAttempts: configuration.GetValue("Coms:MaxAttempts", defaultValue: 5),
                    InitialDelay: TimeSpan.Zero,
                    RetryBaseDelay: Milliseconds(configuration, "Coms:RetryBaseDelayMs", 5000),
                    Consumers: 1,
                    // A send job is one template render and one transport call:
                    // seconds, not minutes. 30s is comfortably past that, so a
                    // dead consumer's send is picked up quickly.
                    ClaimMinIdle: Milliseconds(configuration, "Coms:ClaimMinIdleMs", 30_000)),

                // Four consumers, and no lease: generation jobs are
                // independent, so every worker replica consumes this queue.
                new JobQueueDefinition(
                    "docs",
                    MaxAttempts: configuration.GetValue("Docs:MaxAttempts", defaultValue: 3),
                    InitialDelay: Milliseconds(configuration, "Docs:InitialDelayMs", 1000),
                    RetryBaseDelay: Milliseconds(configuration, "Docs:RetryBaseDelayMs", 2000),
                    Consumers: configuration.GetValue("Docs:Consumers", defaultValue: 4),
                    // A render is a PDF layout plus an S3 upload, and there are
                    // four sibling loops that could reclaim it. Ten minutes is
                    // far past any legitimate render, because the cost of being
                    // wrong is asymmetric: reclaiming a still-running render
                    // duplicates its version row and its S3 object, while
                    // waiting too long only delays recovery from a genuinely
                    // dead consumer. A mid-flight heartbeat is what would let
                    // this come back down; it remains a follow-up.
                    ClaimMinIdle: Milliseconds(configuration, "Docs:ClaimMinIdleMs", 600_000)),
            ],
        };
    }

    private static TimeSpan Milliseconds(IConfiguration configuration, string key, int fallback) =>
        TimeSpan.FromMilliseconds(configuration.GetValue(key, defaultValue: fallback));
}
