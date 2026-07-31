namespace Hsm.Infrastructure.Jobs;

/// <summary>
/// One queue's posture, preserving the frozen BullMQ job options.
/// </summary>
/// <param name="Name">Queue name — the first segment of its jobs' [JobName].</param>
/// <param name="MaxAttempts">Total attempts before the job is dead-lettered.</param>
/// <param name="InitialDelay">Delay before the FIRST attempt (frozen BullMQ 'delay').</param>
/// <param name="RetryBaseDelay">Base of the exponential backoff: delay * 2^(attempt-1).</param>
/// <param name="Consumers">Consume loops this host runs for the queue. 1 means
/// STRICTLY SERIAL, which is load-bearing for <c>coms</c>: send and resend jobs
/// can target the same batch, and a resend that overtakes the earlier job's
/// retry lets that retry's stale FAILED write land after the resend's SENT
/// write. Serial therefore means serial ACROSS RETRIES too — see
/// <see cref="JobQueueDefinition.IsSerial"/>.</param>
public sealed record JobQueueDefinition(
    string Name,
    int MaxAttempts,
    TimeSpan InitialDelay,
    TimeSpan RetryBaseDelay,
    int Consumers)
{
    /// <summary>
    /// One consumer, and no newer job may start while an earlier one is still
    /// working through its attempts. Without the second half the first half is
    /// nearly meaningless: a job that fails goes off to wait out its backoff,
    /// and a job enqueued a moment later — a resend of the very batch that just
    /// failed — would run in the gap and be overwritten when the retry lands.
    /// </summary>
    public bool IsSerial => Consumers <= 1;
}

/// <summary>
/// Where the queues live in Redis and how they behave. One instance per host,
/// bound from configuration.
///
/// <para>Keys are <c>{KeyPrefix}:jobs:{queue}</c> (jobs enqueued now),
/// <c>…:delayed</c> (the sorted set of jobs waiting out a first-attempt delay
/// or a retry backoff), <c>…:due</c> (jobs whose wait has elapsed, drained
/// ahead of new ones) and <c>…:dead</c> (the dead-letter stream). Two streams
/// rather than one because a stream is append-only: a retry promoted back onto
/// the live stream lands BEHIND every job enqueued while it was waiting, which
/// is exactly the overtaking a serial queue must not allow. Every consumer
/// joins the single group
/// <see cref="ConsumerGroup"/> under its own <see cref="ConsumerName"/>: the
/// group is what makes one job run once across N processes, the name is what
/// lets XAUTOCLAIM tell a crashed consumer's work from a live one's.</para>
/// </summary>
public sealed class JobQueueTopology
{
    /// <summary>The one consumer group every worker joins.</summary>
    public const string ConsumerGroup = "hsm-workers";

    /// <summary>Namespace for every key. Deployments sharing a Redis set this apart.</summary>
    public string KeyPrefix { get; init; } = "hsm";

    /// <summary>This process's identity inside the group.</summary>
    public string ConsumerName { get; init; } =
        $"{Environment.MachineName}-{Environment.ProcessId}-{Guid.NewGuid():N}";

    /// <summary>How long an idle consume loop waits before reading again.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(100);

    /// <summary>How often <see cref="DelayedJobPump"/> promotes due delayed jobs.</summary>
    public TimeSpan DelayedPumpInterval { get; init; } = TimeSpan.FromMilliseconds(100);

    /// <summary>How long a delivered-but-unacknowledged job must sit before another
    /// consumer may XAUTOCLAIM it — i.e. how long "that consumer might still be
    /// working" is assumed before "that consumer died" is concluded.</summary>
    public TimeSpan ClaimMinIdle { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Approximate cap on the live/dead streams so history cannot grow
    /// without bound. Approximate trimming never drops recent entries.</summary>
    public int StreamMaxLength { get; init; } = 100_000;

    public IReadOnlyList<JobQueueDefinition> Queues { get; init; } = [];

    public JobQueueDefinition For(string queue) =>
        Queues.FirstOrDefault(q => string.Equals(q.Name, queue, StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"No job queue named '{queue}' is configured.");

    public string StreamKey(string queue) => $"{KeyPrefix}:jobs:{queue}";

    public string DelayedKey(string queue) => $"{KeyPrefix}:jobs:{queue}:delayed";

    /// <summary>Jobs whose delay has elapsed. Drained before <see cref="StreamKey"/>.</summary>
    public string DueKey(string queue) => $"{KeyPrefix}:jobs:{queue}:due";

    public string DeadLetterKey(string queue) => $"{KeyPrefix}:jobs:{queue}:dead";
}
