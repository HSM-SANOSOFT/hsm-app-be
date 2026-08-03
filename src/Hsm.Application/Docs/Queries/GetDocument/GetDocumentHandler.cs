using Hsm.Application.Abstractions;
using Hsm.Application.Errors;
using Hsm.Domain.Docs;

namespace Hsm.Application.Docs.Queries.GetDocument;

public sealed class GetDocumentHandler(IDocumentStore store, ICurrentPrincipal principal)
    : IRequestHandler<GetDocumentQuery, Document>
{
    public async Task<Document> HandleAsync(GetDocumentQuery request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = principal.Actor ?? throw ApiException.Unauthorized();
        var userId = Guid.Parse(actor.Id);

        return await store.FindWithVersionsAsync(request.Id, userId, ct)
            ?? throw ApiException.NotFound($"Document '{request.Id}' not found");
    }
}
