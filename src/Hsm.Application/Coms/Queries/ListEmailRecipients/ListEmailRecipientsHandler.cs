using Hsm.Application.Abstractions;
using Hsm.Domain.Coms;

namespace Hsm.Application.Coms.Queries.ListEmailRecipients;

public sealed class ListEmailRecipientsHandler(IEmailBatchStore store)
    : IRequestHandler<ListEmailRecipientsQuery, IReadOnlyList<EmailRecipient>>
{
    public Task<IReadOnlyList<EmailRecipient>> HandleAsync(
        ListEmailRecipientsQuery request, CancellationToken ct) =>
        store.ListRecipientsAsync(request.Filter, ct);
}
