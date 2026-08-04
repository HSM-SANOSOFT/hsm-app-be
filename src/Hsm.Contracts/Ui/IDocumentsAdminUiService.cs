namespace Hsm.Contracts.Ui;

/// <summary>
/// Document management (plan U18, screen 5): list, upload, retrieve via
/// presigned URL, delete. The host implementation calls the docs handlers in
/// process; the listing keeps the frozen createdBy scoping, so the screen
/// shows the documents the signed-in admin owns.
/// </summary>
public interface IDocumentsAdminUiService
{
    Task<PagedResult<DocumentRowDto>> ListDocumentsAsync(
        int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Uploads the files and returns the created document ids.</summary>
    Task<IReadOnlyList<string>> UploadAsync(
        IReadOnlyList<UploadFileDto> files, CancellationToken cancellationToken = default);

    /// <summary>A time-limited presigned URL for the document's latest version.</summary>
    Task<string> GetDownloadUrlAsync(string documentId, CancellationToken cancellationToken = default);

    Task DeleteAsync(string documentId, CancellationToken cancellationToken = default);
}

/// <summary>One document as the screen renders it.</summary>
public sealed record DocumentRowDto(
    string Id,
    string Title,
    string Type,
    string Status,
    DateTimeOffset CreatedAt);

/// <summary>One file to upload; <paramref name="Content"/> is read once.</summary>
public sealed record UploadFileDto(string FileName, string ContentType, long Size, Stream Content);
