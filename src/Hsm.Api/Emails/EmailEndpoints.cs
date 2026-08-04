using Hsm.Application.Abstractions;
using Hsm.Application.Coms;
using Hsm.Application.Coms.Commands.DispatchEmailBatch;
using Hsm.Application.Coms.Commands.ResendEmailBatch;
using Hsm.Application.Coms.Commands.ResendEmailRecipient;
using Hsm.Application.Coms.Commands.SendEmail;
using Hsm.Application.Coms.Queries.GetEmailBatch;
using Hsm.Application.Coms.Queries.GetEmailRecipient;
using Hsm.Application.Coms.Queries.ListEmails;
using Hsm.Application.Ports;
using Hsm.Contracts;

namespace Hsm.Api.Emails;

/// <summary>
/// The emails resource — a batch IS an email send, so there is no separate
/// "batch" noun at this boundary. Every delegate does transport work only:
/// bind, dispatch, project, choose a status. There is no authentication call
/// and no role check here — the actor is installed by middleware and the
/// policy rides on the request type (every Coms command below is
/// authenticated-only, no role restriction; see ComsRequestPolicyTests).
/// </summary>
public static class EmailEndpoints
{
    public static void MapEmailEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var emails = app.MapGroup("/api/v1/emails").WithTags("Emails");

        emails.MapPost("/", SendEmail)
            .WithSummary("Send an email batch.")
            .Produces<AcceptedEmailResponse>(StatusCodes.Status202Accepted);

        emails.MapGet("/", ListEmails)
            .WithSummary("List email batches, newest first.")
            .Produces<PagedResult<EmailResource>>();

        emails.MapGet("/{id:guid}", GetEmail)
            .WithSummary("Read one email batch with its recipients.")
            .Produces<EmailDetailResource>();

        emails.MapPost("/{id:guid}/resend", ResendEmail)
            .WithSummary("Resend an entire email batch.")
            .Produces<AcceptedJobResponse>(StatusCodes.Status202Accepted);

        emails.MapPost("/{id:guid}/recipients/{recipientId:guid}/resend", ResendRecipient)
            .WithSummary("Resend a single recipient of an email batch.")
            .Produces<AcceptedJobResponse>(StatusCodes.Status202Accepted);
    }

    private static async Task<IResult> SendEmail(
        SendEmailRequest request, IDispatcher dispatcher, IJobQueue queue, CancellationToken ct)
    {
        var result = await dispatcher.Send(
            new SendEmailCommand(
                request.FromEmail, request.FromName, request.To, request.EmailTemplate,
                request.Data, request.DocumentIds),
            ct);

        // Enqueued HERE, after dispatch returns — TransactionBehavior commits
        // the batch only once HandleAsync returns (see SendEmailHandler's doc
        // comment), so enqueuing any earlier would race that commit.
        // CancellationToken.None, deliberately: this is post-commit work — the
        // batch row already exists — and must not be abandoned just because
        // the client hung up between the commit and this call.
        await queue.EnqueueAsync(
            new DispatchEmailBatchCommand(result.BatchId, RecipientId: null), CancellationToken.None);
        return Results.Accepted(
            $"/api/v1/emails/{result.BatchId}", new AcceptedEmailResponse(result.BatchId, result.JobId));
    }

    private static async Task<IResult> ListEmails(
        IDispatcher dispatcher,
        CancellationToken ct,
        Guid? templateId = null,
        string? overallStatus = null,
        Guid? createdBy = null,
        DateTimeOffset? fromDate = null,
        DateTimeOffset? toDate = null,
        int page = PagingRules.DefaultPage,
        int pageSize = PagingRules.DefaultPageSize)
    {
        var filter = new EmailListFilter(templateId, overallStatus, createdBy, fromDate, toDate);
        var result = await dispatcher.Send(new ListEmailsQuery(filter, page, pageSize), ct);
        return Results.Ok(result.Map(EmailResource.From));
    }

    private static async Task<IResult> GetEmail(Guid id, IDispatcher dispatcher, CancellationToken ct) =>
        Results.Ok(EmailDetailResource.From(await dispatcher.Send(new GetEmailBatchQuery(id), ct)));

    private static async Task<IResult> ResendEmail(
        Guid id, IDispatcher dispatcher, IJobQueue queue, CancellationToken ct)
    {
        var jobId = await dispatcher.Send(new ResendEmailBatchCommand(id), ct);
        // Same post-commit enqueue ordering as SendEmail, and for the same
        // reason (ResendEmailBatchHandler's doc comment).
        await queue.EnqueueAsync(
            new DispatchEmailBatchCommand(id, RecipientId: null), CancellationToken.None);
        return Results.Accepted(uri: null, new AcceptedJobResponse(jobId));
    }

    private static async Task<IResult> ResendRecipient(
        Guid id, Guid recipientId, IDispatcher dispatcher, CancellationToken ct)
    {
        // The route's {id} implies recipientId belongs to this batch, but
        // ResendEmailRecipientCommand targets the recipient alone and never
        // checks it — verified here, at the HTTP layer, not in the handler
        // (the handler is correct for direct dispatch; this is a containment
        // check the URL's shape promises). Same 404 shape as an unknown
        // recipient, so a caller can't distinguish "no such recipient" from
        // "exists, but not under this batch".
        var recipient = await dispatcher.Send(new GetEmailRecipientQuery(recipientId), ct);
        if (recipient.BatchId != id)
        {
            throw ComsErrors.RecipientNotFound(recipientId);
        }

        // No caller-side enqueue here: ResendEmailRecipientHandler enqueues
        // DispatchEmailBatchCommand itself (see its doc comment) — it
        // persists nothing, so there is no antecedent write for an
        // endpoint-side enqueue to race.
        var jobId = await dispatcher.Send(new ResendEmailRecipientCommand(recipientId), ct);
        return Results.Accepted(uri: null, new AcceptedJobResponse(jobId));
    }
}
