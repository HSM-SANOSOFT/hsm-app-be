using Hsm.Application.Abstractions;
using Hsm.Contracts;

namespace Hsm.Application.Coms.Queries.ListEmails;

public sealed class ListEmailsHandler(IEmailBatchStore store)
    : IRequestHandler<ListEmailsQuery, PagedResult<EmailBatchSummary>>
{
    public Task<PagedResult<EmailBatchSummary>> HandleAsync(ListEmailsQuery request, CancellationToken ct) =>
        store.ListEmailsAsync(request.Filter, request.Page, request.PageSize, ct);
}
