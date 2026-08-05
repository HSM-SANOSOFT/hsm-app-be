using Hsm.Application.Abstractions;
using Hsm.Application.Identity;
using Hsm.Application.Coms;
using Hsm.Domain.Coms;

namespace Hsm.Application.Coms.Commands.ResendEmailBatch;

/// <summary>
/// Does not enqueue the send-email job itself — see
/// <see cref="Commands.SendEmail.SendEmailHandler"/>'s doc comment for why:
/// <c>TransactionBehavior</c> commits only after this handler returns, so
/// enqueuing here would race that commit. <c>ComsEndpoints.ResendBatch</c>
/// enqueues after <c>dispatcher.Send</c> returns.
/// </summary>
public sealed class ResendEmailBatchHandler(
    IEmailBatchStore store, IUnitOfWork unitOfWork)
    : IRequestHandler<ResendEmailBatchCommand, string>
{
    public async Task<string> HandleAsync(ResendEmailBatchCommand request, CancellationToken ct)
    {
        var batch = await store.FindAsync(request.Id, withRecipients: false, ct)
            ?? throw ComsErrors.BatchNotFound(request.Id);

        var jobId = JobId.New();
        batch.JobId = jobId;
        batch.OverallStatus = EmailBatchStatus.Pending;
        await unitOfWork.SaveChangesAsync(ct);
        return jobId;
    }
}
