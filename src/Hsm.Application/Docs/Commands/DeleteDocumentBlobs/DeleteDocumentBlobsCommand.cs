using Hsm.Application.Abstractions;
using Hsm.Application.Docs.Commands.DeleteDocument;

namespace Hsm.Application.Docs.Commands.DeleteDocumentBlobs;

/// <summary>
/// The 'docs' queue's post-commit blob cleanup for a deleted document.
/// <see cref="DeleteDocumentHandler"/> deliberately does not delete blobs
/// itself (see its own doc comment) — object-store deletes are not
/// transactional, so running them inside the same ambient transaction as a
/// bulk-delete batch would permanently destroy a blob even if a LATER id in
/// the batch failed and rolled the row back. This job runs the actual
/// deletes, enqueued by the endpoint strictly after the transaction that
/// soft-deleted the row(s) has committed.
///
/// <para>Authenticated, same as every other Docs job
/// (<see cref="Hsm.Application.Docs.Commands.RenderDocument.RenderDocumentCommand"/>):
/// the worker dispatches it through <c>IDispatcher</c> with the actor that was
/// current when the delete was enqueued.</para>
/// </summary>
[JobName("docs.delete-blobs")]
public sealed record DeleteDocumentBlobsCommand(
    Guid DocumentId, IReadOnlyList<DocumentBlobRef> Blobs) : ICommand<Unit>;
