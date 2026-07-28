using System.Text.Json.Nodes;
using Hsm.Application.Auth;
using Hsm.Application.Errors;
using Hsm.Application.Templates;
using Hsm.Domain.Coms;

namespace Hsm.Application.Coms;

/// <summary>Shared 404 factories for the coms read surface.</summary>
/// <remarks>
/// The frozen source *intended* 404 for unknown ids (findOneOrFail with a
/// "→ 404" comment, and the contract snapshot declares 404 on all four {id}
/// operations) but its exception filter never caught EntityNotFoundError, so
/// the runtime leaked a 500. The snapshot's declared 404 is honored here — a
/// deliberate, documented divergence from the frozen bug.
/// </remarks>
public static class ComsErrors
{
    public static ApiException BatchNotFound(Guid id) =>
        ApiException.NotFound($"Email batch with id {id} not found");

    public static ApiException RecipientNotFound(Guid id) =>
        ApiException.NotFound($"Email recipient with id {id} not found");
}

/// <summary>
/// Frozen sendEmail: resolve template (404), validate data against its schema
/// BEFORE anything is persisted or dispatched (400 'Template data validation
/// failed'), then batch + per-recipient rows in one transaction, then enqueue.
/// Suppressed addresses are recorded as SUPPRESSED and never dispatched —
/// completing the frozen TODO(U8) as the plan requires.
/// </summary>
public sealed class SendEmailHandler(
    ITemplateStore templates,
    IEmailBatchStore batches,
    IEmailSuppressionStore suppressions,
    IComsJobDispatcher queue,
    IAuthUnitOfWork unitOfWork)
{
    public sealed record Command(
        string? FromEmail,
        string? FromName,
        IReadOnlyList<string> ToEmails,
        string EmailTemplate,
        JsonObject Data,
        IReadOnlyList<string>? DocumentIds);

    public async Task<(Guid BatchId, string JobId)> HandleAsync(
        Command command, Guid? userId, CancellationToken ct = default)
    {
        var template = await templates.FindByIdentifierAsync(command.EmailTemplate, ct: ct)
            ?? throw TemplateErrors.NotFound(command.EmailTemplate);

        var issues = TemplateSchema.Validate(JsonNode.Parse(template.SchemaJson), command.Data);
        if (issues.Count > 0)
        {
            // Frozen envelope: the issue carries only the message (the frozen
            // filter dropped the issues array from the thrown payload).
            throw new ApiException(400, "Template data validation failed");
        }

        var suppressed = await suppressions.SuppressedAmongAsync([.. command.ToEmails], ct);
        // The job id is reserved BEFORE the transaction (it depends on nothing
        // post-commit) so it persists with the batch in ONE commit; the job
        // itself is enqueued only after that commit.
        var jobId = queue.ReserveSendEmailJobId();
        var batch = new EmailBatch
        {
            Id = Guid.NewGuid(),
            TemplateId = template.Id,
            FromEmail = command.FromEmail,
            FromName = command.FromName,
            DataJson = command.Data.ToJsonString(),
            DocumentIds = command.DocumentIds is null ? null : [.. command.DocumentIds],
            JobId = jobId,
            OverallStatus = EmailBatchStatus.Pending,
            CreatedBy = userId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        foreach (var toEmail in command.ToEmails)
        {
            batch.Recipients.Add(new EmailRecipient
            {
                Id = Guid.NewGuid(),
                BatchId = batch.Id,
                ToEmail = toEmail,
                Status = suppressed.Contains(toEmail, StringComparer.Ordinal)
                    ? EmailRecipientStatus.Suppressed
                    : EmailRecipientStatus.Pending,
            });
        }

        await unitOfWork.ExecuteInTransactionAsync(
            async innerCt =>
            {
                await batches.AddAsync(batch, innerCt);
                await unitOfWork.SaveChangesAsync(innerCt);
                return true;
            },
            ct);

        await queue.EnqueueSendEmailAsync(jobId, batch.Id, recipientId: null, ct);

        return (batch.Id, jobId);
    }
}

/// <summary>Frozen listBatches: createdAt DESC with optional filters.</summary>
public sealed class ListEmailBatchesHandler(IEmailBatchStore store)
{
    public Task<IReadOnlyList<EmailBatch>> HandleAsync(BatchListFilter filter, CancellationToken ct = default) =>
        store.ListAsync(filter, ct);
}

/// <summary>Frozen getBatch: the batch with its recipient rows.</summary>
public sealed class GetEmailBatchHandler(IEmailBatchStore store)
{
    public async Task<EmailBatch> HandleAsync(Guid id, CancellationToken ct = default) =>
        await store.FindAsync(id, withRecipients: true, ct)
            ?? throw ComsErrors.BatchNotFound(id);
}

/// <summary>
/// Frozen resendBatch: re-enqueues the whole batch (the dispatcher re-targets
/// PENDING/FAILED rows — no recipient rows are duplicated) and resets the
/// batch to PENDING with the fresh job id.
/// </summary>
public sealed class ResendEmailBatchHandler(IEmailBatchStore store, IComsJobDispatcher queue, IAuthUnitOfWork unitOfWork)
{
    public async Task<string> HandleAsync(Guid id, CancellationToken ct = default)
    {
        var batch = await store.FindAsync(id, withRecipients: false, ct)
            ?? throw ComsErrors.BatchNotFound(id);

        var jobId = queue.ReserveSendEmailJobId();
        batch.JobId = jobId;
        batch.OverallStatus = EmailBatchStatus.Pending;
        await unitOfWork.SaveChangesAsync(ct);
        await queue.EnqueueSendEmailAsync(jobId, id, recipientId: null, ct);
        return jobId;
    }
}

/// <summary>Frozen listRecipients: id ASC with optional filters.</summary>
public sealed class ListEmailRecipientsHandler(IEmailBatchStore store)
{
    public Task<IReadOnlyList<EmailRecipient>> HandleAsync(
        RecipientListFilter filter, CancellationToken ct = default) =>
        store.ListRecipientsAsync(filter, ct);
}

/// <summary>Frozen getRecipient.</summary>
public sealed class GetEmailRecipientHandler(IEmailBatchStore store)
{
    public async Task<EmailRecipient> HandleAsync(Guid id, CancellationToken ct = default) =>
        await store.FindRecipientAsync(id, ct)
            ?? throw ComsErrors.RecipientNotFound(id);
}

/// <summary>
/// Frozen resendRecipient: re-enqueues exactly one recipient of its existing
/// batch — no new batch, no new recipient rows, no status reset here (the
/// dispatcher targets that recipient regardless of its current status).
/// </summary>
public sealed class ResendEmailRecipientHandler(IEmailBatchStore store, IComsJobDispatcher queue)
{
    public async Task<string> HandleAsync(Guid id, CancellationToken ct = default)
    {
        var recipient = await store.FindRecipientAsync(id, ct)
            ?? throw ComsErrors.RecipientNotFound(id);

        var jobId = queue.ReserveSendEmailJobId();
        await queue.EnqueueSendEmailAsync(jobId, recipient.BatchId, recipient.Id, ct);
        return jobId;
    }
}
