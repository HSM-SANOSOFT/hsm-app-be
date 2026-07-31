namespace Hsm.Application.Abstractions;

/// <summary>
/// Mints the job id a client sees (frozen <c>jobId</c> on send/resend/generate
/// responses, and the value persisted on <c>email_batch.job_id</c>).
///
/// <para>It is minted here, not by the queue, because it has to exist BEFORE
/// the enqueue: the id is persisted in the same transaction as the batch or
/// document it identifies, and the enqueue only happens after that transaction
/// commits. Asking the queue for an id first would mean an XADD inside the
/// transaction — the exact ordering bug (a consumer reading rows that are not
/// committed yet) that moving the enqueue to the endpoint fixed.</para>
///
/// <para>It is a correlation id, not a queue handle. The frozen API surfaced
/// BullMQ's counter and never offered a route to look a job up by it, so
/// nothing reads it back; the queue's own identifier is the stream entry id
/// returned by <c>IJobQueue.EnqueueAsync</c>, which appears in logs. Minting
/// randomly rather than counting is what makes the value unique across
/// processes, which a per-process counter was not.</para>
/// </summary>
public static class JobId
{
    public static string New() => Guid.NewGuid().ToString("N");
}
