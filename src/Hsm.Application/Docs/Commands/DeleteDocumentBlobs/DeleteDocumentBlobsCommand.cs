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
///
/// <para><see cref="NoAmbientTransactionAttribute"/> for the opposite reason to
/// the other carriers: not "a write must survive a refusal" but "there is no
/// write at all". <c>DeleteDocumentBlobsHandler</c> touches nothing but
/// <see cref="Hsm.Application.Ports.IObjectStorage"/>, so without this
/// <c>TransactionBehavior</c> would open a real Postgres transaction and hold a
/// connection idle across up to four concurrent S3 round trips for a job that
/// never issues a statement.</para>
/// </summary>
[JobName("docs.delete-blobs")]
[NoAmbientTransaction]
public sealed record DeleteDocumentBlobsCommand(
    Guid DocumentId, IReadOnlyList<DocumentBlobRef> Blobs) : ICommand<Unit>;
