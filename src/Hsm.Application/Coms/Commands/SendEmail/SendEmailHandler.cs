using System.Text.Json.Nodes;
using FluentValidation;
using Hsm.Application.Abstractions;
using Hsm.Application.Auth;
using Hsm.Application.Errors;
using Hsm.Application.Templates;
using Hsm.Domain.Coms;

namespace Hsm.Application.Coms.Commands.SendEmail;

/// <summary>
/// <b>Does not enqueue the send-email job itself</b> — unlike the frozen
/// handler and the pre-slicing port, which enqueued right after
/// <c>SaveChangesAsync</c>. <c>TransactionBehavior</c> wraps this whole handler
/// in one transaction that commits only after <c>HandleAsync</c> returns, so
/// enqueuing here would race the commit: the consumer runs in another process
/// entirely and could look up the new batch before this transaction commits it.
/// <c>ComsEndpoints.SendEmail</c> enqueues after <c>dispatcher.Send</c>
/// returns, which is a real post-commit point. See
/// <see cref="Commands.ReceiveWebhook.ReceiveWebhookCommand"/>'s handler for
/// the same fix and the contract-test failure that proved it was needed, not
/// merely theoretical.
/// </summary>
public sealed class SendEmailHandler(
    ITemplateStore templates,
    IEmailBatchStore batches,
    IEmailSuppressionStore suppressions,
    IAuthUnitOfWork unitOfWork,
    ICurrentPrincipal principal) : IRequestHandler<SendEmailCommand, SendEmailResult>
{
    public async Task<SendEmailResult> HandleAsync(SendEmailCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var actor = principal.Actor ?? throw new UnauthorizedException();
        var userId = Guid.Parse(actor.Id);

        var template = await templates.FindByIdentifierAsync(request.EmailTemplate, ct: ct)
            ?? throw TemplateErrors.NotFound(request.EmailTemplate);

        var issues = TemplateSchema.Validate(JsonNode.Parse(template.SchemaJson), request.Data);
        if (issues.Count > 0)
        {
            // The schema issues already carry paths — they become per-field
            // errors instead of one prose string.
            throw new ValidationException(
                issues.Select(i => new FluentValidation.Results.ValidationFailure(
                    $"data.{i.Path}", $"expected {i.Expected}, got {i.Received}")));
        }

        var suppressed = await suppressions.SuppressedAmongAsync([.. request.ToEmails], ct);
        // Minted here, no I/O, so it persists WITH the batch in one commit;
        // the enqueue itself is the caller's job, strictly after that commit
        // (see the class doc comment and JobId's).
        var jobId = JobId.New();
        var batch = new EmailBatch
        {
            Id = Guid.NewGuid(),
            TemplateId = template.Id,
            FromEmail = request.FromEmail,
            FromName = request.FromName,
            DataJson = request.Data.ToJsonString(),
            DocumentIds = request.DocumentIds is null ? null : [.. request.DocumentIds],
            JobId = jobId,
            OverallStatus = EmailBatchStatus.Pending,
            CreatedBy = userId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        foreach (var toEmail in request.ToEmails)
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

        await batches.AddAsync(batch, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return new SendEmailResult(batch.Id, jobId);
    }
}
