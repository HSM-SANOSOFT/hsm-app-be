using Hsm.Application.Abstractions;
using Hsm.Application.Coms;
using Hsm.Domain.Coms;

namespace Hsm.Application.Coms.Queries.GetEmailRecipient;

public sealed class GetEmailRecipientHandler(IEmailBatchStore store)
    : IRequestHandler<GetEmailRecipientQuery, EmailRecipient>
{
    public async Task<EmailRecipient> HandleAsync(GetEmailRecipientQuery request, CancellationToken ct) =>
        await store.FindRecipientAsync(request.Id, ct)
            ?? throw ComsErrors.RecipientNotFound(request.Id);
}
