using Hsm.Application.Abstractions;

namespace Hsm.Application.Docs.Commands.UploadDocuments;

/// <summary>
/// POST /v1/docs/upload: filename-matched multipart
/// files, blob uploads first, then ONE transaction for the whole document
/// graph — guaranteeing that a mid-loop failure cannot leave a
/// half-written graph. Authenticated only; the creator is not carried on the
/// command — read from <see cref="ICurrentPrincipal"/> (self-scoped, same as
/// <see cref="Hsm.Application.Docs.Commands.GenerateDocument.GenerateDocumentCommand"/>).
/// </summary>
public sealed record UploadDocumentsCommand(
    IReadOnlyList<UploadPayloadItem> Payload,
    string? EntityId,
    string? EntityType,
    IReadOnlyList<UploadFileUpload> Files) : ICommand<UploadDocumentsResult>;

/// <summary>
/// One multipart file part. <paramref name="Content"/> is the caller's
/// buffered form stream — read once here, disposed with the request.
/// </summary>
public sealed record UploadFileUpload(string FileName, string ContentType, long Size, Stream Content);

public sealed record UploadPayloadFile(string FolderName, string FileName);

public sealed record UploadPayloadItem(string Bucket, IReadOnlyList<UploadPayloadFile> Files);

public sealed record UploadedFile(string FileId, string Filename, string Key);

public sealed record UploadedItem(string Bucket, IReadOnlyList<UploadedFile> Files);

public sealed record UploadDocumentsResult(IReadOnlyList<UploadedItem> S3Result, IReadOnlyList<Guid> DocumentIds);
