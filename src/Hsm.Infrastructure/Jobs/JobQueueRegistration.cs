using Hsm.Application.Abstractions;
using Hsm.Application.Ports;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hsm.Infrastructure.Jobs;

/// <summary>
/// Composition for the job queue: the producing side everywhere, the consuming
/// side only where a host is meant to do background work.
/// </summary>
public static class JobQueueRegistration
{
    /// <summary>
    /// The producing side plus the consumer implementation. Registered by every
    /// host; costs nothing until something enqueues, because
    /// <see cref="RedisJobConnection"/> connects on first use.
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
    /// <b>INTERIM — Task 20 deletes this call and moves consumption to
    /// <c>Hsm.Worker</c>.</b>
    ///
    /// <para>Hosts the consume loop and the delayed pump inside an HTTP host,
    /// which is where background processing lives until the worker takes it
    /// over. It also installs <see cref="AmbientPrincipal"/> so a job scope has
    /// somewhere to put the envelope's actor.</para>
    ///
    /// <para><b>And it namespaces the queue to this host instance.</b> That is
    /// the one honest wart of the interim arrangement. A co-hosted consumer
    /// makes the host both producer and consumer, and the frozen HTTP contract
    /// suites were written against a queue with exactly that shape: several
    /// hosts boot in one test process, against one database and one Redis, each
    /// with its own email-transport double, and each asserts on the jobs IT
    /// enqueued (one suite arms its transport to fail the next five sends and
    /// requires all five failures to be its own). Sharing one namespace across
    /// co-hosted consumers would let one suite's host process another suite's
    /// job — a cross-talk the frozen tests cannot express and must not have to.
    /// So while a consumer lives in the HTTP host, that host keeps its own
    /// namespace, exactly as the in-process channel gave it. The durable,
    /// shared-namespace path is what <c>Hsm.Worker</c> will run and what
    /// <c>JobQueueDurabilityTests</c> proves; this registration disappearing in
    /// Task 20 is what turns it on in production.</para>
    /// </summary>
    public static IServiceCollection AddInHostJobConsumer(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddScoped<AmbientPrincipal>();
        services.AddSingleton(BuildTopology(
            configuration, $"{KeyPrefix(configuration)}:host:{Guid.NewGuid():N}"));
        services.AddHostedService<JobConsumerHostedService>();
        services.AddHostedService<DelayedJobPump>();
        return services;
    }

    private static string KeyPrefix(IConfiguration configuration) =>
        configuration["Jobs:KeyPrefix"] ?? "hsm";

    /// <summary>
    /// The per-queue posture, preserving the frozen BullMQ job options: coms 5
    /// attempts / 5s base / strictly serial; docs 3 attempts / 1s first-attempt
    /// delay / 2s base / four consumers. Every value is deployment-tunable
    /// because the contract suites tighten the timings.
    /// </summary>
    private static JobQueueTopology BuildTopology(
        IConfiguration configuration, string? keyPrefixOverride = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return new JobQueueTopology
        {
            KeyPrefix = keyPrefixOverride ?? KeyPrefix(configuration),
            PollInterval = Milliseconds(configuration, "Jobs:PollIntervalMs", 100),
            DelayedPumpInterval = Milliseconds(configuration, "Jobs:DelayedPumpIntervalMs", 100),
            StreamMaxLength = configuration.GetValue("Jobs:StreamMaxLength", defaultValue: 100_000),
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
                    // dead consumer. Task 20's heartbeat is what would let this
                    // come back down.
                    ClaimMinIdle: Milliseconds(configuration, "Docs:ClaimMinIdleMs", 600_000)),
            ],
        };
    }

    private static TimeSpan Milliseconds(IConfiguration configuration, string key, int fallback) =>
        TimeSpan.FromMilliseconds(configuration.GetValue(key, defaultValue: fallback));
}
