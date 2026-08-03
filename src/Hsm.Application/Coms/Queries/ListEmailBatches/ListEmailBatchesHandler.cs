using Hsm.Application.Abstractions;
using Hsm.Domain.Coms;

namespace Hsm.Application.Coms.Queries.ListEmailBatches;

public sealed class ListEmailBatchesHandler(IEmailBatchStore store)
    : IRequestHandler<ListEmailBatchesQuery, IReadOnlyList<EmailBatch>>
{
    public Task<IReadOnlyList<EmailBatch>> HandleAsync(ListEmailBatchesQuery request, CancellationToken ct) =>
        store.ListAsync(request.Filter, ct);
}
