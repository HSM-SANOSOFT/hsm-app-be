using Hsm.Application.Abstractions;
using Hsm.Application.Errors;
using Hsm.Application.Ports;

namespace Hsm.Application.Docs.Queries.GetDocumentUrl;

public sealed class GetDocumentUrlHandler(IDocumentStore store, IObjectStorage storage, ICurrentPrincipal principal)
    : IRequestHandler<GetDocumentUrlQuery, string>
{
    public async Task<string> HandleAsync(GetDocumentUrlQuery request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = principal.Actor ?? throw ApiException.Unauthorized();
        var userId = Guid.Parse(actor.Id);

        var document = await store.FindWithVersionsAsync(request.Id, userId, ct)
            ?? throw ApiException.NotFound($"Document '{request.Id}' not found");

        var latest = document.Versions.OrderByDescending(v => v.Version).FirstOrDefault();
        if (latest?.Storage is null)
        {
            throw ApiException.NotFound($"No generated file found for document '{request.Id}'");
        }

        var (folderName, fileId) = StorageKeys.Split(latest.Storage.Path);
        var url = await storage.PresignGetAsync(
            StorageKeys.MakeKey(folderName, fileId),
            expiresIn: null,
            contentDisposition: "inline",
            bucket: latest.Storage.Bucket,
            cancellationToken: ct);
        return url.AbsoluteUri;
    }
}
