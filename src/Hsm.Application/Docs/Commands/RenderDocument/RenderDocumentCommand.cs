using Hsm.Application.Abstractions;

namespace Hsm.Application.Docs.Commands.RenderDocument;

/// <summary>
/// The frozen worker 'document' job envelope (frozen BullMQ 'document' queue,
/// job name <c>render</c>). <see cref="JobNameAttribute"/> is what the job
/// registry maps to and from — the payload never names a CLR type.
///
/// <para>Authenticated, and ENFORCED: dispatched through <c>IDispatcher</c>
/// with the actor that was current when POST /v1/docs/generate enqueued it,
/// carried in the job envelope. The two reasons Task 13 recorded for bypassing
/// the pipeline are both resolved: the envelope now carries the enqueuing
/// <c>RequestActor</c>, and <see cref="NoAmbientTransactionAttribute"/> keeps
/// the pipeline from opening a transaction that the handler's re-throw would
/// roll the FAILED status (and the template parse-log row) back out of — so
/// <c>GenerateDocumentContractTests.Unknown_template_marks_the_document_failed</c>
/// and its siblings still read FAILED.</para>
///
/// <para>Carries identifiers and the render data the caller already supplied —
/// no credentials. Nothing secret may be added to it: it is serialized onto a
/// shared queue.</para>
/// </summary>
[JobName("docs.render")]
[NoAmbientTransaction]
public sealed record RenderDocumentCommand(
    Guid DocumentId,
    string TemplateIdentifier,
    string DataJson,
    string? EntityId,
    string? EntityType) : ICommand<Unit>;
