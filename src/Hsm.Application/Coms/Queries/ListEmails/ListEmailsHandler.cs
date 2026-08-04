using Hsm.Application.Abstractions;
using Hsm.Application.Coms;
using Hsm.Contracts;
using Hsm.Domain.Coms;

namespace Hsm.Application.Coms.Queries.ListEmails;

public sealed class ListEmailsHandler(IEmailBatchStore store)
    : IRequestHandler<ListEmailsQuery, PagedResult<EmailBatch>>
{
    public Task<PagedResult<EmailBatch>> HandleAsync(ListEmailsQuery request, CancellationToken ct) =>
        store.ListEmailsAsync(request.Filter, request.Page, request.PageSize, ct);
}
