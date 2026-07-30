using Hsm.Application.Abstractions;

namespace Hsm.Application.Docs.Commands.RenderDocument;

/// <summary>
/// The frozen worker 'document' job envelope (frozen BullMQ 'document' queue,
/// job name <c>render</c>). Carries <see cref="JobNameAttribute"/> for
/// Task 19's job-name → type registry.
///
/// Authenticated per the plan's conversion table — but NOT enforced today.
/// <c>DocsJobProcessor.RunAsync</c> (see
/// <see cref="Hsm.Infrastructure.Docs.DocsJobProcessor"/>) resolves and calls
/// this command's handler directly, bypassing <c>IDispatcher</c>/the
/// pipeline entirely, for the same two reasons Task 12 documented for
/// Coms's send-email job:
///
/// <list type="number">
/// <item>There is no actor to install: the in-process channel queue (<see
/// cref="Hsm.Application.Docs.GenerateDocumentJob"/>) carries no caller
/// identity, and extending it to capture one is a queue-shape change this
/// task must not make (Task 19 owns the queue). Routing through
/// <c>AuthorizationBehavior</c> with no actor would 401 every job.</item>
/// <item>Even with an actor, <c>TransactionBehavior</c> would be actively
/// wrong here: on a render failure this handler persists the document's
/// FAILED status and then re-throws so <c>ChannelJobProcessor</c>'s retry
/// loop can count the attempt and back off. Wrapping the handler in one
/// pipeline-owned transaction would roll that FAILED write back on the very
/// re-throw that is supposed to carry it forward — silently breaking
/// <c>GenerateDocumentContractTests.Unknown_template_marks_the_document_failed</c>
/// and its siblings, which assert the FAILED status survives.</item>
/// </list>
///
/// Task 19 must resolve both before this policy takes effect: it needs to
/// decide how the real actor is carried on the wire and how retry-then-fail
/// semantics coexist with a pipeline-owned transaction.
/// </summary>
[JobName("docs.render")]
public sealed record RenderDocumentCommand(
    Guid DocumentId,
    string TemplateIdentifier,
    string DataJson,
    string? EntityId,
    string? EntityType) : ICommand<Unit>;
