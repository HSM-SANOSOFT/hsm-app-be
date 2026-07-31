using System.Text.Json;
using Hsm.Application.Abstractions;
using Hsm.Application.Ports;
using StackExchange.Redis;

namespace Hsm.Infrastructure.Jobs;

/// <summary>
/// The durable <see cref="IJobQueue"/>: XADD onto <c>{prefix}:jobs:{queue}</c>,
/// returning the stream entry id as the job id.
///
/// <para>This replaces an in-process <c>Channel</c>. The channel lost every
/// queued send and generation the moment its process stopped, and with N host
/// instances each held its own invisible queue — a regression from the frozen
/// BullMQ behavior that no test caught because every test ran one process.
/// A stream entry survives the process that wrote it, and a consumer group
/// makes exactly one consumer — in any process — take each entry.</para>
///
/// <para>The enqueuing actor is read from <see cref="ICurrentPrincipal"/> HERE,
/// at the edge, and travels in the envelope: that is the whole reason a queued
/// command can go through <c>AuthorizationBehavior</c> at all (before this, job
/// commands bypassed the pipeline because there was no actor to authorize
/// against). Nothing else about the caller is captured — no token, no cookie,
/// no credentials.</para>
///
/// <para>A queue with an <see cref="JobQueueDefinition.InitialDelay"/> (the
/// frozen document queue's 1s) goes onto the delayed set instead of the stream,
/// so the delay is stored rather than slept through by the enqueuing request.</para>
/// </summary>
// CA1711: named for what it is — the Redis Streams implementation of
// IJobQueue, which carries the same suppressed suffix for the same reason.
#pragma warning disable CA1711
public sealed class RedisStreamJobQueue(
    IJobConnection connection,
    JobNameRegistry registry,
    JobQueueTopology topology,
    ICurrentPrincipal principal) : IJobQueue
{
    public async Task<string> EnqueueAsync<TCommand>(TCommand command, CancellationToken ct = default)
        where TCommand : ICommand<Unit>
    {
        ArgumentNullException.ThrowIfNull(command);

        var jobName = registry.NameOf(command.GetType());
        var queue = JobNameRegistry.QueueOf(jobName);
        var definition = topology.For(queue);
        var envelope = new JobEnvelope(
            jobName,
            JsonSerializer.Serialize(command, command.GetType(), JobJson.Options),
            principal.Actor,
            Attempt: 1);

        var db = await connection.GetDatabaseAsync().ConfigureAwait(false);
        return definition.InitialDelay > TimeSpan.Zero
            ? await ScheduleAsync(db, topology, queue, envelope, definition.InitialDelay).ConfigureAwait(false)
            : await AddAsync(db, topology, topology.StreamKey(queue), envelope).ConfigureAwait(false);
    }

    /// <summary>XADDs the envelope onto <paramref name="streamKey"/>.</summary>
    internal static async Task<string> AddAsync(
        IDatabase db, JobQueueTopology topology, string streamKey, JobEnvelope envelope)
    {
        var id = await db.StreamAddAsync(
            streamKey,
            JobEnvelope.Field,
            envelope.ToJson(),
            messageId: null,
            maxLength: topology.StreamMaxLength,
            useApproximateMaxLength: true).ConfigureAwait(false);
        return id.ToString();
    }

    /// <summary>
    /// ZADDs the envelope onto the queue's delayed set, scored with the wall
    /// clock at which it becomes due. <see cref="DelayedJobPump"/> moves it onto
    /// the due stream then.
    /// </summary>
    internal static async Task<string> ScheduleAsync(
        IDatabase db, JobQueueTopology topology, string queue, JobEnvelope envelope, TimeSpan delay)
    {
        var delayed = new DelayedJob(Guid.NewGuid().ToString("N"), envelope);
        await db.SortedSetAddAsync(
            topology.DelayedKey(queue),
            delayed.ToJson(),
            DateTimeOffset.UtcNow.Add(delay).ToUnixTimeMilliseconds()).ConfigureAwait(false);
        return delayed.Id;
    }
}
#pragma warning restore CA1711
