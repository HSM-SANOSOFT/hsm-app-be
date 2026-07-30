using Hsm.Application.Abstractions;

namespace Hsm.Application.Coms.Commands.DispatchEmailBatch;

/// <summary>
/// The frozen worker 'send-email' job envelope (frozen BullMQ 'coms' queue,
/// job name <c>send-email</c>). Carries <see cref="JobNameAttribute"/> for
/// Task 19's job-name → type registry.
///
/// Authenticated per the plan's conversion table — but NOT enforced today.
/// <c>ComsJobProcessor.RunAsync</c> (see <see cref="Hsm.Infrastructure.Coms.ComsJobProcessor"/>)
/// resolves and calls this command's handler directly, bypassing
/// <c>IDispatcher</c>/the pipeline entirely, for two reasons:
///
/// <list type="number">
/// <item>There is no actor to install: the in-process channel queue (<see
/// cref="Hsm.Infrastructure.Coms.ComsJob.SendEmail"/>) carries only
/// <c>(BatchId, RecipientId)</c> — no caller identity — and extending it to
/// capture one is a queue-shape change this task must not make (Task 19
/// owns the queue). Routing through <c>AuthorizationBehavior</c> with no
/// actor would 401 every job.</item>
/// <item>Even with an actor, <c>TransactionBehavior</c> would be actively
/// wrong here: on a transport failure this handler persists the recipient's
/// FAILED status and then re-throws so <c>ChannelJobProcessor</c>'s retry
/// loop can count the attempt and back off. Wrapping the handler in one
/// pipeline-owned transaction would roll that FAILED write back on the very
/// re-throw that is supposed to carry it forward — silently breaking
/// <c>BatchAndRecipientContractTests.Resending_a_failed_recipient_dispatches_again_without_duplicating_the_batch</c>,
/// which asserts the FAILED status and its error message survive every
/// exhausted retry.</item>
/// </list>
///
/// Task 19 must resolve both before this policy takes effect: it needs to
/// decide how the real actor is carried on the wire and how retry-then-fail
/// semantics coexist with a pipeline-owned transaction.
/// </summary>
[JobName("coms.send-email")]
public sealed record DispatchEmailBatchCommand(Guid BatchId, Guid? RecipientId) : ICommand<Unit>;
