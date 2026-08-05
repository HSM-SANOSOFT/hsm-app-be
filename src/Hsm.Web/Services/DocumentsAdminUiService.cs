using Hsm.Application.Abstractions;
using Hsm.Application.Docs;
using Hsm.Application.Docs.Commands.DeleteDocument;
using Hsm.Application.Docs.Commands.DeleteDocumentBlobs;
using Hsm.Application.Docs.Commands.UploadDocuments;
using Hsm.Application.Docs.Queries.GetDocumentUrl;
using Hsm.Application.Docs.Queries.ListDocuments;
using Hsm.Application.Errors;
using Hsm.Application.Ports;
using Hsm.Contracts;
using Hsm.Contracts.Ui;
using Hsm.Web.Auth;

namespace Hsm.Web.Services;

/// <summary>
/// Host-side document management (plan U18, screen 5): publish the actor,
/// then dispatch the same requests the /v1/docs endpoints do, with the same
/// createdBy scoping bound to the signed-in admin (see
/// <see cref="ShellActor"/>). Uploads land in the configured docs bucket under
/// a fixed folder — the screen does not expose bucket/folder choice.
/// </summary>
public sealed class DocumentsAdminUiService(
    ShellActor shellActor,
    IDispatcher dispatcher,
    IJobQueue jobQueue,
    DocsOptions docsOptions) : IDocumentsAdminUiService
{
    /// <summary>Where screen uploads land inside the docs bucket.</summary>
    public const string UploadFolder = "admin-uploads";

    public async Task<PagedResult<DocumentRowDto>> ListDocumentsAsync(
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var actor = await shellActor.InstallAsync(cancellationToken)
            ?? throw new UnauthorizedException();
        // CreatedBy scoping: this screen lists the signed-in admin's own
        // uploads. Whether that caller may list at all is
        // ListDocumentsQuery's policy, decided in the pipeline a line later.
        var adminId = Guid.Parse(actor.Id);
        var filter = new DocumentListFilter(
            adminId, EntityId: null, EntityType: null, Type: null, Status: null);
        var result = await dispatcher.Send(new ListDocumentsQuery(filter, page, pageSize), cancellationToken);
        return result.Map(d => new DocumentRowDto(d.Id.ToString(), d.Title, d.Type, d.Status, d.CreatedAt));
    }

    public async Task<IReadOnlyList<string>> UploadAsync(
        IReadOnlyList<UploadFileDto> files, CancellationToken cancellationToken = default)
    {
        await shellActor.InstallAsync(cancellationToken);
        if (files.Count == 0)
        {
            return [];
        }

        // Buffer each browser stream: the S3 adapter needs a length, and the
        // handler's filename matching consumes streams in payload order.
        var uploads = new List<UploadFileUpload>(files.Count);
        try
        {
            foreach (var file in files)
            {
                var buffer = new MemoryStream();
                await file.Content.CopyToAsync(buffer, cancellationToken);
                buffer.Position = 0;
                uploads.Add(new UploadFileUpload(
                    file.FileName, file.ContentType, buffer.Length, buffer));
            }

            var command = new UploadDocumentsCommand(
                Payload:
                [
                    new UploadPayloadItem(
                        docsOptions.Bucket,
                        [.. uploads.Select(u => new UploadPayloadFile(UploadFolder, u.FileName))]),
                ],
                EntityId: null,
                EntityType: null,
                Files: uploads);
            var result = await dispatcher.Send(command, cancellationToken);
            return [.. result.DocumentIds.Select(id => id.ToString())];
        }
        finally
        {
            foreach (var staged in uploads)
            {
                await staged.Content.DisposeAsync();
            }
        }
    }

    public async Task<string> GetDownloadUrlAsync(
        string documentId, CancellationToken cancellationToken = default)
    {
        await shellActor.InstallAsync(cancellationToken);
        return await dispatcher.Send(new GetDocumentUrlQuery(Guid.Parse(documentId)), cancellationToken);
    }

    public async Task DeleteAsync(string documentId, CancellationToken cancellationToken = default)
    {
        await shellActor.InstallAsync(cancellationToken);
        var result = await dispatcher.Send(new DeleteDocumentCommand(Guid.Parse(documentId)), cancellationToken);

        // Same post-commit-enqueue rule as DocumentEndpoints.DeleteDocument:
        // DeleteDocumentHandler no longer deletes blobs itself (see its own
        // doc comment) — this door must schedule the cleanup exactly the way
        // the REST door does, or an admin-screen delete would leak the blob
        // forever.
        if (result.Blobs.Count > 0)
        {
            await jobQueue.EnqueueAsync(
                new DeleteDocumentBlobsCommand(result.DocumentId, result.Blobs), CancellationToken.None);
        }
    }
}
