using Hsm.Application.Docs;
using Hsm.Contracts.Ui;

namespace Hsm.Web.Services;

/// <summary>
/// Host-side document management (plan U18, screen 5): admin gate first, then
/// the same handlers the frozen /v1/docs endpoints call, with the frozen
/// createdBy scoping bound to the signed-in admin. Uploads land in the
/// configured docs bucket under a fixed folder — the screen does not expose
/// bucket/folder choice.
/// </summary>
public sealed class DocumentsAdminUiService(
    UiServiceGate gate,
    ListDocumentsHandler list,
    UploadDocumentsHandler upload,
    GetDocumentUrlHandler getUrl,
    DeleteDocumentHandler delete,
    DocsOptions docsOptions) : IDocumentsAdminUiService
{
    /// <summary>Where screen uploads land inside the docs bucket.</summary>
    public const string UploadFolder = "admin-uploads";

    public async Task<DocumentListPageDto> ListDocumentsAsync(
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var adminId = await gate.RequireAdminIdAsync();
        var filter = new DocumentListFilter(
            adminId, EntityId: null, EntityType: null, Type: null, Status: null, page, pageSize);
        var (items, total) = await list.HandleAsync(filter, cancellationToken);
        return new DocumentListPageDto(
            [.. items.Select(d => new DocumentRowDto(
                d.Id.ToString(), d.Title, d.Type, d.Status, d.CreatedAt))],
            page,
            pageSize,
            total);
    }

    public async Task<IReadOnlyList<string>> UploadAsync(
        IReadOnlyList<UploadFileDto> files, CancellationToken cancellationToken = default)
    {
        var adminId = await gate.RequireAdminIdAsync();
        if (files.Count == 0)
        {
            return [];
        }

        // Buffer each browser stream: the S3 adapter needs a length, and the
        // handler's filename matching consumes streams in payload order.
        var uploads = new List<UploadDocumentsHandler.FileUpload>(files.Count);
        try
        {
            foreach (var file in files)
            {
                var buffer = new MemoryStream();
                await file.Content.CopyToAsync(buffer, cancellationToken);
                buffer.Position = 0;
                uploads.Add(new UploadDocumentsHandler.FileUpload(
                    file.FileName, file.ContentType, buffer.Length, buffer));
            }

            var command = new UploadDocumentsHandler.Command(
                Payload:
                [
                    new UploadDocumentsHandler.PayloadItem(
                        docsOptions.Bucket,
                        [.. uploads.Select(u =>
                            new UploadDocumentsHandler.PayloadFile(UploadFolder, u.FileName))]),
                ],
                EntityId: null,
                EntityType: null,
                Files: uploads);
            var result = await upload.HandleAsync(command, adminId, cancellationToken);
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
        var adminId = await gate.RequireAdminIdAsync();
        return await getUrl.HandleAsync(Guid.Parse(documentId), adminId, cancellationToken);
    }

    public async Task DeleteAsync(string documentId, CancellationToken cancellationToken = default)
    {
        var adminId = await gate.RequireAdminIdAsync();
        await delete.HandleAsync(Guid.Parse(documentId), adminId, cancellationToken);
    }
}
