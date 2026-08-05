using Hsm.Domain.Coms;

namespace Hsm.Api.Emails;

/// <summary>
/// The wire shape of an email batch. A batch IS an email send — the
/// "batch" noun dies at the HTTP boundary, so this resource is what
/// <c>/api/v1/emails</c> serves both from its list and its detail route.
///
/// <para><see cref="TemplateId"/> is named for what it actually is — a raw FK,
/// exposed the same way <see cref="CreatedBy"/> is — rather than "EmailTemplate",
/// which would collide in meaning with <c>SendEmailRequest.EmailTemplate</c>
/// (a human-readable identifier resolved by <c>SendEmailHandler</c> via
/// <c>ITemplateStore.FindByIdentifierAsync</c>). Resolving that identifier back
/// out of a bare <c>TemplateId</c> would need a cross-module lookup no consumer
/// has asked for; this field stays the id until that's real. <see cref="UpdatedAt"/>
/// has no backing column either — the entity tracks only <c>CreatedAt</c> — so
/// it is the batch's <c>CreatedAt</c> repeated, which is honest about "we don't
/// track updates" rather than fabricating a distinct value.</para>
/// </summary>
public sealed record EmailResource(
    Guid Id, string? FromEmail, string? FromName, Guid? TemplateId,
    string OverallStatus, int TotalRecipients, int SentCount, int FailedCount,
    Guid? CreatedBy, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    public static EmailResource From(EmailBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        return new EmailResource(
            batch.Id,
            batch.FromEmail,
            batch.FromName,
            batch.TemplateId,
            batch.OverallStatus,
            batch.Recipients.Count,
            // SENT and DELIVERED both count as sent (DispatchEmailBatchHandler's
            // own rule — a recipient moves SENT -> DELIVERED when the
            // provider's webhook lands, and that is not a regression back to
            // "not sent").
            batch.Recipients.Count(r => r.Status is EmailRecipientStatus.Sent or EmailRecipientStatus.Delivered),
            batch.Recipients.Count(r => r.Status == EmailRecipientStatus.Failed),
            batch.CreatedBy,
            batch.CreatedAt,
            batch.CreatedAt);
    }
}

public sealed record EmailDetailResource(
    Guid Id, string? FromEmail, string? FromName, Guid? TemplateId,
    string OverallStatus, int TotalRecipients, int SentCount, int FailedCount,
    Guid? CreatedBy, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    IReadOnlyList<EmailRecipientResource> Recipients)
{
    public static EmailDetailResource From(EmailBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var summary = EmailResource.From(batch);
        return new EmailDetailResource(
            summary.Id, summary.FromEmail, summary.FromName, summary.TemplateId,
            summary.OverallStatus, summary.TotalRecipients, summary.SentCount, summary.FailedCount,
            summary.CreatedBy, summary.CreatedAt, summary.UpdatedAt,
            [.. batch.Recipients.OrderBy(r => r.Id).Select(r => EmailRecipientResource.From(r, batch.CreatedAt))]);
    }
}

/// <summary>
/// <see cref="CreatedAt"/>/<see cref="UpdatedAt"/> have no backing columns on
/// <see cref="EmailRecipient"/> either. Recipients are created atomically with
/// their batch (<c>SendEmailHandler</c> adds every row in the same
/// transaction), so the batch's <c>CreatedAt</c> is a true value for
/// <see cref="CreatedAt"/> here, not an invented one. <see cref="UpdatedAt"/>
/// falls back to the same value when the recipient has never recorded a
/// <c>SentAt</c> — the one state-change timestamp the entity does carry.
/// </summary>
public sealed record EmailRecipientResource(
    Guid Id, string ToEmail, string Status, string? ProviderMessageId,
    string? FailureReason, DateTimeOffset? SentAt, DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static EmailRecipientResource From(EmailRecipient recipient, DateTimeOffset batchCreatedAt)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        return new EmailRecipientResource(
            recipient.Id,
            recipient.ToEmail,
            recipient.Status,
            recipient.MessageId,
            recipient.ErrorMessage,
            recipient.SentAt,
            batchCreatedAt,
            recipient.SentAt ?? batchCreatedAt);
    }
}

/// <summary>Request bodies. Bound by System.Text.Json; validated by the pipeline.</summary>
public sealed record SendEmailRequest(
    string? FromEmail, string? FromName, IReadOnlyList<string> To,
    string EmailTemplate, System.Text.Json.Nodes.JsonObject Data,
    IReadOnlyList<string>? DocumentIds);

public sealed record AcceptedEmailResponse(Guid Id, string JobId);

public sealed record AcceptedJobResponse(string JobId);
