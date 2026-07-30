using Hsm.Application.Abstractions;

namespace Hsm.Application.Coms.Commands.ProcessWebhookEvent;

/// <summary>
/// The frozen worker 'process-webhook-event' job envelope (frozen BullMQ
/// 'coms' queue, job name <c>process-webhook-event</c>). Carries
/// <see cref="JobNameAttribute"/> for Task 19's job-name → type registry.
///
/// <see cref="AllowAnonymousRequestAttribute"/>: this command never carries a
/// caller identity (it names only the already-persisted, already-idempotent
/// webhook event to process) and, like <see cref="Commands.DispatchEmailBatch.DispatchEmailBatchCommand"/>,
/// is not dispatched through <c>IDispatcher</c> today — <c>ComsJobProcessor</c>
/// resolves and calls its handler directly. Unlike the send-email job this
/// command's atomicity does not depend on bypassing the pipeline (its single
/// trailing <c>SaveChangesAsync</c> call is atomic on its own), so the two
/// job commands are treated uniformly for simplicity, not because both
/// require it.
/// </summary>
[JobName("coms.process-webhook")]
[AllowAnonymousRequest]
public sealed record ProcessWebhookEventCommand(Guid WebhookEventId) : ICommand<Unit>;
