using Hsm.Application.Abstractions;

namespace Hsm.Application.Docs.Commands.DeleteDocument;

/// <summary>
/// DELETE /v1/docs/{id} (frozen deleteDocument): owner-scoped 404, then soft
/// delete + best-effort blob deletion of every version's object. Frozen link
/// semantics preserved: links neither block the delete nor cascade.
/// Authenticated only; owner id read from <see cref="ICurrentPrincipal"/> —
/// same rationale as
/// <see cref="Hsm.Application.Docs.Queries.GetDocument.GetDocumentQuery"/>.
/// </summary>
public sealed record DeleteDocumentCommand(Guid Id) : ICommand<Unit>;
