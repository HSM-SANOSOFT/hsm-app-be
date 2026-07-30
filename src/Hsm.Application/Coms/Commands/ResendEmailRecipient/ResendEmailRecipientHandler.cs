using Hsm.Application.Abstractions;
using Hsm.Application.Coms;

namespace Hsm.Application.Coms.Commands.ResendEmailRecipient;

public sealed class ResendEmailRecipientHandler(IEmailBatchStore store, IComsJobDispatcher queue)
    : IRequestHandler<ResendEmailRecipientCommand, string>
{
    public async Task<string> HandleAsync(ResendEmailRecipientCommand request, CancellationToken ct)
    {
        var recipient = await store.FindRecipientAsync(request.Id, ct)
            ?? throw ComsErrors.RecipientNotFound(request.Id);

        var jobId = queue.ReserveSendEmailJobId();
        await queue.EnqueueSendEmailAsync(jobId, recipient.BatchId, recipient.Id, ct);
        return jobId;
    }
}
