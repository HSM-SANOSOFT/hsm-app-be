using Hsm.Application.Abstractions;
using Hsm.Application.Coms;
using Hsm.Application.Coms.Commands.DispatchEmailBatch;
using Hsm.Application.Ports;

namespace Hsm.Application.Coms.Commands.ResendEmailRecipient;

/// <summary>
/// The one enqueue that stays INSIDE its handler, deliberately: this handler
/// persists nothing (there is no status reset here), so there is no antecedent
/// write for the enqueue to race — the recipient row it names was
/// committed by an earlier request. Every other enqueue in this module moved to
/// its endpoint for exactly the reason this one did not have to.
/// </summary>
public sealed class ResendEmailRecipientHandler(IEmailBatchStore store, IJobQueue queue)
    : IRequestHandler<ResendEmailRecipientCommand, string>
{
    public async Task<string> HandleAsync(ResendEmailRecipientCommand request, CancellationToken ct)
    {
        var recipient = await store.FindRecipientAsync(request.Id, ct)
            ?? throw ComsErrors.RecipientNotFound(request.Id);

        await queue.EnqueueAsync(
            new DispatchEmailBatchCommand(recipient.BatchId, recipient.Id), ct);
        return JobId.New();
    }
}
