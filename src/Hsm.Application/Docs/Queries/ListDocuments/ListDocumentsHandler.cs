using Hsm.Application.Abstractions;
using Hsm.Contracts;
using Hsm.Domain.Docs;

namespace Hsm.Application.Docs.Queries.ListDocuments;

public sealed class ListDocumentsHandler(IDocumentStore store)
    : IRequestHandler<ListDocumentsQuery, PagedResult<Document>>
{
    public Task<PagedResult<Document>> HandleAsync(ListDocumentsQuery request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        return store.ListAsync(request.Filter, request.Page, request.PageSize, ct);
    }
}
