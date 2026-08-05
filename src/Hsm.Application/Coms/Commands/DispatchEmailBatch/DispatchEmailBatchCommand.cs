using Hsm.Application.Abstractions;

namespace Hsm.Application.Coms.Commands.DispatchEmailBatch;

/// <summary>
/// The 'send-email' job envelope, on the 'coms' queue.
/// <see cref="JobNameAttribute"/> is what the job registry maps to and from —
/// the payload never names a CLR type.
///
/// <para>Authenticated, and ENFORCED: this command is dispatched through
/// <c>IDispatcher</c> like every other, and the actor it authorizes against is
/// the one that was current when the job was enqueued (the user who called
/// POST /v1/coms/send/email or a resend route), carried in the job envelope and
/// reinstalled by the consumer. Tasks 12 and 13 left this policy as honest but
/// unenforced metadata for two reasons, both now resolved:</para>
///
/// <list type="number">
/// <item>There was no actor to install — the in-process channel job carried
/// only <c>(BatchId, RecipientId)</c>. The envelope now carries the enqueuing
/// <c>RequestActor</c> (identity only: id, roles, onboarding state).</item>
/// <item><c>TransactionBehavior</c> would roll back the FAILED write this
/// handler makes before re-throwing. Resolved at the boundary rather than at
/// the write: <see cref="NoAmbientTransactionAttribute"/> leaves this command's
/// commits to the handler, exactly as they were before the queue routed it
/// through a dispatcher — so
/// <c>BatchAndRecipientContractTests.Resending_a_failed_recipient_dispatches_again_without_duplicating_the_batch</c>
/// still reads FAILED with its error message after five exhausted
/// attempts.</item>
/// </list>
///
/// <para>Carries identifiers only. Nothing about this command is a secret, and
/// nothing about it may become one: it is serialized onto a shared queue.</para>
/// </summary>
[JobName("coms.send-email")]
[NoAmbientTransaction]
public sealed record DispatchEmailBatchCommand(Guid BatchId, Guid? RecipientId) : ICommand<Unit>;
