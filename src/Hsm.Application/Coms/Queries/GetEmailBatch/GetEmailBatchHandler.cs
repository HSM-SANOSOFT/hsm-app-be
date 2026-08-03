using Hsm.Application.Abstractions;
using Hsm.Application.Coms;
using Hsm.Domain.Coms;

namespace Hsm.Application.Coms.Queries.GetEmailBatch;

public sealed class GetEmailBatchHandler(IEmailBatchStore store)
    : IRequestHandler<GetEmailBatchQuery, EmailBatch>
{
    public async Task<EmailBatch> HandleAsync(GetEmailBatchQuery request, CancellationToken ct) =>
        await store.FindAsync(request.Id, withRecipients: true, ct)
            ?? throw ComsErrors.BatchNotFound(request.Id);
}
