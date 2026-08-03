using Hsm.Application.Abstractions;

namespace Hsm.Application.Docs.Queries.ListDocuments;

public sealed class ListDocumentsHandler(IDocumentStore store)
    : IRequestHandler<ListDocumentsQuery, ListDocumentsResult>
{
    public async Task<ListDocumentsResult> HandleAsync(ListDocumentsQuery request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (items, total) = await store.ListAsync(request.Filter, ct);
        return new ListDocumentsResult(items, total);
    }
}
