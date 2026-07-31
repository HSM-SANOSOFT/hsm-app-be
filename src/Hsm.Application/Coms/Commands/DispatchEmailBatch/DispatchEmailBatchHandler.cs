using System.Text.Json.Nodes;
using Hsm.Application.Abstractions;
using Hsm.Application.Auth;
using Hsm.Application.Coms;
using Hsm.Application.Templates;
using Hsm.Domain.Coms;

namespace Hsm.Application.Coms.Commands.DispatchEmailBatch;

/// <summary>
/// The frozen worker 'send-email' job processor (worker EmailService.sendEmail):
/// batch → PROCESSING, target selection (one recipient on a recipient resend,
/// otherwise every PENDING/FAILED row — SUPPRESSED rows are never targeted),
/// template render (failures leave recipient rows untouched and fail the job
/// for retry), one transport send, then SENT/FAILED bulk updates and the
/// aggregate overall-status recomputation.
///
/// <para>Dispatched through <c>IDispatcher</c> — telemetry, authorization
/// against the enqueuing actor, validation — but NOT under a pipeline-owned
/// transaction: the command carries
/// <see cref="NoAmbientTransactionAttribute"/>. Each <c>SaveChangesAsync</c>
/// below commits independently, exactly as the frozen worker's two separate
/// writes did (PROCESSING, then the final SENT/FAILED write), and the failure
/// write below survives the re-throw that carries the attempt back to the
/// queue. Under one pipeline transaction it would not: the same re-throw would
/// roll it back. See the attribute for the full reasoning.</para>
/// </summary>
public sealed class DispatchEmailBatchHandler(
    IEmailBatchStore batches,
    TemplateParser parser,
    IEmailTransport transport,
    IAuthUnitOfWork unitOfWork) : IRequestHandler<DispatchEmailBatchCommand, Unit>
{
    public async Task<Unit> HandleAsync(DispatchEmailBatchCommand request, CancellationToken ct)
    {
        var batch = await batches.FindAsync(request.BatchId, withRecipients: true, ct)
            ?? throw new InvalidOperationException($"EmailBatch {request.BatchId} not found");

        batch.OverallStatus = EmailBatchStatus.Processing;
        await unitOfWork.SaveChangesAsync(ct);

        var targets = request.RecipientId is not null
            ? batch.Recipients.Where(r => r.Id == request.RecipientId).ToList()
            : batch.Recipients
                .Where(r => r.Status is EmailRecipientStatus.Pending or EmailRecipientStatus.Failed)
                .ToList();
        if (targets.Count == 0)
        {
            return Unit.Value;
        }

        // Frozen: attachment resolution consulted the documents module, which
        // arrives with U15 — documentIds round-trip untouched until then.

        // Render BEFORE the failure-marking scope: a template problem retries
        // the job without touching recipient rows (frozen behavior).
        var data = JsonNode.Parse(batch.DataJson) as JsonObject ?? [];
        var render = await parser.ParseEmailAsync(
            batch.TemplateId?.ToString() ?? string.Empty, data, userId: null, ct);

        try
        {
            var messageId = await transport.SendAsync(
                new OutboundEmail(
                    batch.FromEmail,
                    [.. targets.Select(t => t.ToEmail)],
                    render.Subject,
                    render.Html),
                ct);

            var now = DateTimeOffset.UtcNow;
            foreach (var target in targets)
            {
                target.Status = EmailRecipientStatus.Sent;
                target.SentAt = now;
            }

            batch.ProviderMessageId = messageId;
            batch.OverallStatus = ComputeOverallStatus(batch.Recipients);
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The frozen failure write: these recipients settle FAILED with the
            // transport's message, and the batch's overall status follows —
            // then the error propagates so the queue counts the attempt. Both
            // halves matter, which is why this command opts out of the
            // pipeline's transaction. Note this catch covers the TRANSPORT send
            // only: a template render failure is thrown above, outside the try,
            // and leaves recipient rows untouched (frozen behavior).
            foreach (var target in targets)
            {
                target.Status = EmailRecipientStatus.Failed;
                target.ErrorMessage = exception.Message;
            }

            batch.OverallStatus = ComputeOverallStatus(batch.Recipients);
            await unitOfWork.SaveChangesAsync(ct);
            throw;
        }

        return Unit.Value;
    }

    /// <summary>The frozen computeOverallStatus (SENT and DELIVERED both count as sent).</summary>
    internal static string ComputeOverallStatus(IReadOnlyCollection<EmailRecipient> recipients)
    {
        var sent = recipients.Count(r =>
            r.Status is EmailRecipientStatus.Sent or EmailRecipientStatus.Delivered);
        var failed = recipients.Count(r => r.Status == EmailRecipientStatus.Failed);
        var total = recipients.Count;

        if (sent == total)
        {
            return EmailBatchStatus.Sent;
        }

        if (failed == total)
        {
            return EmailBatchStatus.Failed;
        }

        return sent > 0 ? EmailBatchStatus.Partial : EmailBatchStatus.Pending;
    }
}
