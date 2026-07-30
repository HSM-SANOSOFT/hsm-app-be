using Hsm.Application.Abstractions;
using Hsm.Application.Auth;
using Hsm.Application.Coms;
using Hsm.Domain.Coms;

namespace Hsm.Application.Coms.Commands.ResendEmailBatch;

/// <summary>
/// Does not enqueue the send-email job itself — see
/// <see cref="Commands.SendEmail.SendEmailHandler"/>'s doc comment for why:
/// <c>TransactionBehavior</c> commits only after this handler returns, so
/// enqueuing here would race that commit. <see cref="Hsm.Web.Coms.ComsEndpoints.ResendBatch"/>
/// enqueues after <c>dispatcher.Send</c> returns.
/// </summary>
public sealed class ResendEmailBatchHandler(
    IEmailBatchStore store, IComsJobDispatcher queue, IAuthUnitOfWork unitOfWork)
    : IRequestHandler<ResendEmailBatchCommand, string>
{
    public async Task<string> HandleAsync(ResendEmailBatchCommand request, CancellationToken ct)
    {
        var batch = await store.FindAsync(request.Id, withRecipients: false, ct)
            ?? throw ComsErrors.BatchNotFound(request.Id);

        var jobId = queue.ReserveSendEmailJobId();
        batch.JobId = jobId;
        batch.OverallStatus = EmailBatchStatus.Pending;
        await unitOfWork.SaveChangesAsync(ct);
        return jobId;
    }
}
