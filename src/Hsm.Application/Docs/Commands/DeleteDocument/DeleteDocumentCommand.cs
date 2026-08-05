using Hsm.Application.Abstractions;

namespace Hsm.Application.Docs.Commands.DeleteDocument;

/// <summary>
/// DELETE /v1/docs/{id}: owner-scoped 404, then soft delete. Link semantics:
/// links neither block the delete nor cascade. Authenticated only; owner id
/// read from <see cref="ICurrentPrincipal"/>
/// — same rationale as
/// <see cref="Hsm.Application.Docs.Queries.GetDocument.GetDocumentQuery"/>.
///
/// <para>Blob deletion is NOT part of this command — see
/// <see cref="DeleteDocumentHandler"/>'s doc comment for why, and
/// <see cref="Hsm.Application.Docs.Commands.DeleteDocumentBlobs.DeleteDocumentBlobsCommand"/>
/// for where it actually happens.</para>
/// </summary>
public sealed record DeleteDocumentCommand(Guid Id) : ICommand<DeleteDocumentResult>;

/// <summary>
/// What the caller needs to schedule blob cleanup once this command's
/// transaction has actually committed. <see cref="Blobs"/> is empty for a
/// document with no storage objects (e.g. every version failed to render).
/// </summary>
public sealed record DeleteDocumentResult(Guid DocumentId, IReadOnlyList<DocumentBlobRef> Blobs);

/// <summary>One version's object-store coordinates, captured before the row was soft-deleted.</summary>
public sealed record DocumentBlobRef(string Key, string Bucket);
