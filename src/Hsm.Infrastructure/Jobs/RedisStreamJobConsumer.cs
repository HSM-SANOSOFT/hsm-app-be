using System.Collections.Concurrent;
using System.Text.Json;
using Hsm.Application.Abstractions;
using Hsm.Application.Ports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Hsm.Infrastructure.Jobs;

/// <summary>
/// Reads one batch of jobs and settles each of them.
///
/// <para><b>A queued command runs through the pipeline.</b> The consumer
/// installs the envelope's actor into the scope's
/// <see cref="AmbientPrincipal"/> and dispatches through
/// <see cref="IDispatcher"/>, so telemetry, authorization and validation apply
/// to a job exactly as they apply to the HTTP request that queued it. Before
/// this, the channel processor resolved each job handler directly and no policy
/// on a job command meant anything (Task 12 J3, Task 13 J3).</para>
///
/// <para><b>Failure writes still belong to the handlers.</b> The send-email and
/// document-render handlers persist a FAILED status and then re-throw so the
/// attempt is counted — a contract a "commit only on success" transaction
/// cannot hold, since the re-throw rolls the write back. Rather than move every
/// such write out of every such handler (the parse-log row
/// <c>TemplateParser</c> writes has the same problem, and any future one
/// would), those two commands carry
/// <see cref="NoAmbientTransactionAttribute"/> and keep their own commit
/// boundaries. The consumer therefore does nothing special on failure beyond
/// retry accounting.</para>
///
/// <para>Delivery is at-least-once, matching the frozen BullMQ posture: a
/// consumer that dies mid-job leaves its entry pending, XAUTOCLAIM hands it to
/// another consumer after <see cref="JobQueueDefinition.ClaimMinIdle"/>, and
/// the job runs again. Handlers stay idempotent. That threshold is per queue
/// and deliberately generous for docs: without mid-flight lock renewal (still a
/// follow-up) the only thing separating a slow job from a duplicate concurrent
/// run is the threshold exceeding the job's worst-case runtime.</para>
///
/// <para>Who drives this is <c>Hsm.Worker</c>'s <c>JobConsumerService</c>, and
/// nothing else. The request-serving hosts register the queue so they can
/// enqueue; a drain never happens inside one.</para>
/// </summary>
public sealed partial class RedisStreamJobConsumer(
    IJobConnection connection,
    JobNameRegistry registry,
    JobQueueTopology topology,
    IServiceScopeFactory scopeFactory,
    ILogger<RedisStreamJobConsumer> logger) : IJobConsumer
{
    private readonly ConcurrentDictionary<string, bool> _groups = new(StringComparer.Ordinal);

    public async Task<int> DrainOnceAsync(string queue, int max, CancellationToken ct)
    {
        var definition = topology.For(queue);
        var db = await connection.GetDatabaseAsync().ConfigureAwait(false);
        await EnsureGroupsAsync(db, queue).ConfigureAwait(false);

        // Promote anything whose delay has elapsed before reading, so one drain
        // is self-sufficient: the hosted pump keeps an idle host moving, but a
        // caller driving DrainOnceAsync in a loop needs no second thread.
        await DelayedJobPump.PumpAsync(db, topology, queue).ConfigureAwait(false);

        // Due work — retries, and first attempts that waited out the frozen
        // initial delay — is drained BEFORE anything enqueued since.
        var processed = await DrainStreamAsync(
            db, definition, topology.DueKey(queue), max, ct).ConfigureAwait(false);
        if (processed >= max)
        {
            return processed;
        }

        // A serial queue does not start newer work while an earlier job is
        // still working through its attempts: the whole point of coms being
        // serial is that a resend must not run in the gap between a failed
        // attempt and its retry, only to be overwritten when the retry lands.
        // The waiting job's state is in Redis, not in this process — nothing is
        // blocked in memory and another consumer can pick it up.
        if (definition.IsSerial
            && (processed > 0 || await IsRetryingAsync(db, definition).ConfigureAwait(false)))
        {
            return processed;
        }

        return processed + await DrainStreamAsync(
            db, definition, topology.StreamKey(queue), max - processed, ct).ConfigureAwait(false);
    }

    /// <summary>Is a job of this queue mid-attempts — waiting out a backoff, or due but unprocessed?</summary>
    private async Task<bool> IsRetryingAsync(IDatabase db, JobQueueDefinition definition)
    {
        if (await db.SortedSetLengthAsync(topology.DelayedKey(definition.Name)).ConfigureAwait(false) > 0)
        {
            return true;
        }

        var pending = await db.StreamPendingAsync(
            topology.DueKey(definition.Name), JobQueueTopology.ConsumerGroup).ConfigureAwait(false);
        return pending.PendingMessageCount > 0;
    }

    private async Task<int> DrainStreamAsync(
        IDatabase db, JobQueueDefinition definition, string streamKey, int max, CancellationToken ct)
    {
        if (max <= 0)
        {
            return 0;
        }

        var processed = 0;
        foreach (var entry in await ReadAsync(db, definition, streamKey, max).ConfigureAwait(false))
        {
            await ProcessAsync(db, definition, streamKey, entry, ct).ConfigureAwait(false);
            processed++;
        }

        return processed;
    }

    /// <summary>
    /// The groups are created at 0-0, not at each stream's tail: a job enqueued
    /// before any consumer ever ran — the exact case the channel queue lost —
    /// must be delivered, not skipped.
    /// </summary>
    private async Task EnsureGroupsAsync(IDatabase db, string queue)
    {
        if (_groups.ContainsKey(queue))
        {
            return;
        }

        foreach (var key in new[] { topology.StreamKey(queue), topology.DueKey(queue) })
        {
            try
            {
                await db.StreamCreateConsumerGroupAsync(
                    key,
                    JobQueueTopology.ConsumerGroup,
                    StreamPosition.Beginning,
                    createStream: true).ConfigureAwait(false);
            }
            catch (RedisServerException exception)
                when (exception.Message.Contains("BUSYGROUP", StringComparison.Ordinal))
            {
                // Another consumer created it first — that is the normal case.
            }
        }

        _groups[queue] = true;
    }

    /// <summary>
    /// Reclaimed work first (XAUTOCLAIM: entries a consumer took and never
    /// acknowledged, because it crashed), then new work (XREADGROUP '&gt;').
    /// </summary>
    private async Task<StreamEntry[]> ReadAsync(
        IDatabase db, JobQueueDefinition definition, string key, int max)
    {
        // The idle threshold is the queue's, not a global one: it has to exceed
        // the queue's worst-case job runtime or a slow job gets a second,
        // concurrent run. See JobQueueDefinition.ClaimMinIdle.
        var claimed = await db.StreamAutoClaimAsync(
            key,
            JobQueueTopology.ConsumerGroup,
            topology.ConsumerName,
            (long)definition.ClaimMinIdle.TotalMilliseconds,
            StreamPosition.Beginning,
            max).ConfigureAwait(false);

        var reclaimed = claimed.ClaimedEntries ?? [];
        if (reclaimed.Length >= max)
        {
            return reclaimed;
        }

        var fresh = await db.StreamReadGroupAsync(
            key,
            JobQueueTopology.ConsumerGroup,
            topology.ConsumerName,
            StreamPosition.NewMessages,
            max - reclaimed.Length).ConfigureAwait(false) ?? [];

        return reclaimed.Length == 0 ? fresh : [.. reclaimed, .. fresh];
    }

    private async Task ProcessAsync(
        IDatabase db,
        JobQueueDefinition definition,
        string streamKey,
        StreamEntry entry,
        CancellationToken ct)
    {
        var raw = entry[JobEnvelope.Field];

        JobEnvelope envelope;
        ICommand<Unit> command;
        try
        {
            envelope = JobEnvelope.FromJson(raw!);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // An envelope this consumer cannot even read is not retryable — a
            // later attempt would fail identically. Dead-letter it verbatim so
            // the payload is still there to look at.
            LogUnreadable(logger, exception, definition.Name, entry.Id.ToString());
            await DeadLetterAsync(db, definition.Name, raw).ConfigureAwait(false);
            await db.StreamAcknowledgeAsync(
                streamKey, JobQueueTopology.ConsumerGroup, entry.Id).ConfigureAwait(false);
            return;
        }

        if (!registry.TryTypeOf(envelope.JobName, out var commandType))
        {
            // A name this process has never heard of is the ordinary shape of a
            // ROLLING DEPLOY: a producer that has already been updated enqueues
            // a job whose handler only exists in the new worker image, and an
            // old worker picks it up. Dead-lettering there would destroy real
            // work for the length of a deploy, so it is rescheduled with the
            // queue's normal backoff instead — by which time the worker that
            // takes it is likely the new one. If the name is genuinely bogus
            // the attempts still run out and it still dead-letters; the only
            // thing traded is a few minutes.
            LogUnknownJobName(logger, definition.Name, envelope.JobName, envelope.Attempt);
            await FailAsync(
                db,
                definition,
                envelope,
                new InvalidOperationException(
                    $"No handler for job name '{envelope.JobName}' is deployed in this worker."))
                .ConfigureAwait(false);
            await db.StreamAcknowledgeAsync(
                streamKey, JobQueueTopology.ConsumerGroup, entry.Id).ConfigureAwait(false);
            return;
        }

        try
        {
            command = (ICommand<Unit>)JsonSerializer.Deserialize(
                envelope.Payload, commandType, JobJson.Options)!;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A known name whose payload will not deserialize into it: the
            // shape is wrong, and no retry changes that.
            LogUnreadable(logger, exception, definition.Name, entry.Id.ToString());
            await DeadLetterAsync(db, definition.Name, raw).ConfigureAwait(false);
            await db.StreamAcknowledgeAsync(
                streamKey, JobQueueTopology.ConsumerGroup, entry.Id).ConfigureAwait(false);
            return;
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            // The enqueuing actor, reinstalled: this is what lets
            // AuthorizationBehavior judge a job the way it judged its request.
            scope.ServiceProvider.GetRequiredService<AmbientPrincipal>().Set(envelope.Actor);
            await scope.ServiceProvider.GetRequiredService<IDispatcher>()
                .Send(command, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The host is stopping. Leave the entry unacknowledged so it stays
            // in the group's pending list and XAUTOCLAIM redelivers it.
            throw;
        }
        catch (Exception exception)
        {
            await FailAsync(db, definition, envelope, exception).ConfigureAwait(false);
        }

        // Acknowledged either way: a settled job (done, rescheduled, or
        // dead-lettered) must leave the pending list, or XAUTOCLAIM would
        // resurrect it alongside its own retry.
        await db.StreamAcknowledgeAsync(
            streamKey, JobQueueTopology.ConsumerGroup, entry.Id).ConfigureAwait(false);
    }

    /// <summary>Reschedules with exponential backoff, or dead-letters when attempts are spent.</summary>
    private async Task FailAsync(
        IDatabase db, JobQueueDefinition definition, JobEnvelope envelope, Exception failure)
    {
        if (envelope.Attempt < definition.MaxAttempts)
        {
            // Frozen backoff: base * 2^(attempt-1).
            var delay = definition.RetryBaseDelay * Math.Pow(2, envelope.Attempt - 1);
            LogRetry(logger, definition.Name, envelope.JobName, envelope.Attempt, failure.Message);
            await RedisStreamJobQueue.ScheduleAsync(
                db, topology, definition.Name, envelope with { Attempt = envelope.Attempt + 1 }, delay)
                .ConfigureAwait(false);
            return;
        }

        LogDeadLettered(logger, failure, definition.Name, envelope.JobName, envelope.Attempt);
        await DeadLetterAsync(db, definition.Name, envelope.ToJson()).ConfigureAwait(false);
    }

    private async Task DeadLetterAsync(IDatabase db, string queue, RedisValue payload) =>
        await db.StreamAddAsync(
            topology.DeadLetterKey(queue),
            JobEnvelope.Field,
            payload,
            messageId: null,
            maxLength: topology.StreamMaxLength,
            useApproximateMaxLength: true).ConfigureAwait(false);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "{Queue} job {JobName} attempt {Attempt} failed: {Reason}; retrying")]
    private static partial void LogRetry(
        ILogger logger, string queue, string jobName, int attempt, string reason);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "{Queue} job {JobName} failed after {Attempts} attempts; moved to the dead-letter stream")]
    private static partial void LogDeadLettered(
        ILogger logger, Exception exception, string queue, string jobName, int attempts);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "{Queue} job name '{JobName}' is not deployed in this worker (attempt {Attempt}); rescheduling")]
    private static partial void LogUnknownJobName(
        ILogger logger, string queue, string jobName, int attempt);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "{Queue} entry {EntryId} could not be read as a job envelope; dead-lettered verbatim")]
    private static partial void LogUnreadable(
        ILogger logger, Exception exception, string queue, string entryId);

}
