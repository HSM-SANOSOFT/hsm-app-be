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
/// <see cref="JobQueueDefinition.IsSerial"/>, and read its scope limit before
/// relying on it.</param>
/// <param name="ClaimMinIdle">How long a delivered-but-unacknowledged job must
/// sit before another consumer may XAUTOCLAIM it — i.e. how long "that consumer
/// might still be working" is assumed before "that consumer died" is concluded.
/// It is per-queue because it must exceed the queue's WORST-CASE job runtime:
/// reclaiming a job that is merely slow runs it a second time concurrently,
/// which for a document render means a duplicate version row and a duplicate S3
/// object. See <see cref="JobQueueTopology"/> for the tradeoff this buys.</param>
public sealed record JobQueueDefinition(
    string Name,
    int MaxAttempts,
    TimeSpan InitialDelay,
    TimeSpan RetryBaseDelay,
    int Consumers,
    TimeSpan ClaimMinIdle)
{
    /// <summary>
    /// One consume loop, and no newer job may start while an earlier one is
    /// still working through its attempts. Without the second half the first
    /// half is nearly meaningless: a job that fails goes off to wait out its
    /// backoff, and a job enqueued a moment later — a resend of the very batch
    /// that just failed — would run in the gap and be overwritten when the retry
    /// lands.
    ///
    /// <para><b>What this property alone gives you is one PROCESS.</b> The
    /// no-newer-work rule is enforced by a consumer deciding not to read new
    /// work while it can see scheduled or due entries. Two consuming processes
    /// make that decision independently, both see an empty delayed set at the
    /// same instant, and both take a job — nothing in this file is a
    /// distributed lock, and <c>Consumers</c> configures one host's loop count,
    /// not the cluster's.</para>
    ///
    /// <para><b>Cluster-wide seriality is the consumer's lease.</b>
    /// <c>Hsm.Worker</c>'s <c>JobConsumerService</c> runs a serial queue's loop
    /// only while it holds <see cref="JobQueueTopology.LeaseKey"/> for that
    /// queue (<c>SET NX PX</c>, renewed while held, released on shutdown), so N
    /// worker replicas still consume the queue one at a time. Without it,
    /// scaling the worker horizontally silently reintroduces the overtaking
    /// this property exists to prevent.</para>
    /// </summary>
    public bool IsSerial => Consumers <= 1;
}

/// <summary>
/// Where the queues live in Redis and how they behave. One instance per host,
/// bound from configuration.
///
/// <para><b>KeyPrefix is the deployment's namespace, and it is shared.</b>
/// Every producer (<c>Hsm.Api</c>, <c>Hsm.Web</c>) enqueues into it and
/// <c>Hsm.Worker</c> consumes it, so a restarted producer strands nothing:
/// what it wrote is still there for the worker. It is plain configuration
/// (<c>Jobs:KeyPrefix</c>) with a fixed default, never derived per host or per
/// process — that would give each host a private queue nobody else can see,
/// which is exactly the defect this queue replaced. Test hosts that must not
/// see each other's jobs set the key themselves.</para>
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
///
/// <para><b>On reclaiming.</b> XAUTOCLAIM cannot tell a dead consumer from a
/// slow one; it can only measure idle time. There is no mid-flight lock renewal
/// here — the frozen BullMQ worker renewed its lock while a job ran, so a long
/// job was never mistaken for a lost one — so the only thing standing between a
/// slow job and a duplicate run is
/// <see cref="JobQueueDefinition.ClaimMinIdle"/> being larger than the job can
/// take. It is set per queue for that reason, and generously for docs, at the
/// cost of recovery latency: a consumer that really does die leaves its job
/// unclaimed for that long. A heartbeat (periodic XCLAIM by the running
/// consumer, which would let the threshold drop to seconds again) is a
/// follow-up, and is NOT part of Task 20 — the worker inherits this threshold
/// unchanged.</para>
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

    /// <summary>Approximate cap on the live/dead streams so history cannot grow
    /// without bound. Approximate trimming never drops recent entries.</summary>
    public int StreamMaxLength { get; init; } = 100_000;

    /// <summary>
    /// How long a consumer's claim on a serial queue survives without renewal.
    /// The worker renews at a third of it while it is running and releases it on
    /// clean shutdown, so this value is only ever spent by a worker that DIED
    /// holding it: it is the queue's failover window, during which no replica
    /// consumes and jobs simply wait in Redis. Longer means slower failover;
    /// shorter risks a paused process (a long GC, a stalled Redis round trip)
    /// losing a lease it still believes it holds, which would put two consumers
    /// on a queue whose whole point is that there is one.
    /// </summary>
    public TimeSpan LeaseTtl { get; init; } = TimeSpan.FromSeconds(15);

    public IReadOnlyList<JobQueueDefinition> Queues { get; init; } = [];

    public JobQueueDefinition For(string queue) =>
        Queues.FirstOrDefault(q => string.Equals(q.Name, queue, StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"No job queue named '{queue}' is configured.");

    public string StreamKey(string queue) => $"{KeyPrefix}:jobs:{queue}";

    public string DelayedKey(string queue) => $"{KeyPrefix}:jobs:{queue}:delayed";

    /// <summary>Jobs whose delay has elapsed. Drained before <see cref="StreamKey"/>.</summary>
    public string DueKey(string queue) => $"{KeyPrefix}:jobs:{queue}:due";

    public string DeadLetterKey(string queue) => $"{KeyPrefix}:jobs:{queue}:dead";

    /// <summary>
    /// Who is allowed to consume a serial queue right now. Held by exactly one
    /// worker replica at a time (<see cref="LeaseTtl"/>); a queue that is not
    /// serial has no lease and every replica consumes it.
    /// </summary>
    public string LeaseKey(string queue) => $"{KeyPrefix}:lease:{queue}";

    /// <summary>
    /// Who already fired a scheduled job for the current tick. Written
    /// <c>NX PX</c> at just under the schedule's interval, so N workers running
    /// the same schedule fire it once and the key is gone before the next tick.
    /// </summary>
    public string ScheduleKey(string name) => $"{KeyPrefix}:sched:{name}";
}
