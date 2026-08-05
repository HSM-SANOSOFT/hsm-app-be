using Hsm.Application.Abstractions;

namespace Hsm.Application.Coms.Commands.ProcessWebhookEvent;

/// <summary>
/// The 'process-webhook-event' job envelope, on the 'coms' queue.
/// <see cref="JobNameAttribute"/> is what the job registry maps to and from.
///
/// <para><see cref="AllowAnonymousRequestAttribute"/>, and now genuinely
/// enforced: this job is enqueued by the @Public provider webhook, so the
/// envelope's actor is legitimately null. It names only the already-persisted,
/// already-idempotent webhook event to process — identifiers only, nothing
/// secret on the queue. Being anonymous is exactly why it can run without an
/// actor while <see cref="Commands.DispatchEmailBatch.DispatchEmailBatchCommand"/>
/// cannot.</para>
/// </summary>
[JobName("coms.process-webhook")]
[AllowAnonymousRequest]
public sealed record ProcessWebhookEventCommand(Guid WebhookEventId) : ICommand<Unit>;
