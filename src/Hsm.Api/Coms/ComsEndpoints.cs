using System.Text.Json.Nodes;
using Hsm.Api.Auth;
using Hsm.Api.Http;
using Hsm.Application;
using Hsm.Application.Abstractions;
using Hsm.Application.Coms;
using Hsm.Application.Coms.Commands.DispatchEmailBatch;
using Hsm.Application.Coms.Commands.ProcessWebhookEvent;
using Hsm.Application.Coms.Commands.ReceiveWebhook;
using Hsm.Application.Coms.Commands.ResendEmailBatch;
using Hsm.Application.Coms.Commands.ResendEmailRecipient;
using Hsm.Application.Coms.Commands.SendEmail;
using Hsm.Application.Coms.Queries.GetEmailBatch;
using Hsm.Application.Coms.Queries.GetEmailRecipient;
using Hsm.Application.Coms.Queries.ListEmailBatches;
using Hsm.Application.Coms.Queries.ListEmailRecipients;
using Hsm.Application.Ports;
using Hsm.Domain.Coms;

namespace Hsm.Api.Coms;

/// <summary>
/// The nine frozen /v1/coms operations (coms.controller.ts +
/// coms-webhook.controller.ts). All authenticated (any role, onboarded)
/// except the provider webhook, which is @Public. POST routes return 201.
/// The frozen list envelope quirk is preserved: the service's total/page/limit
/// were dropped by the response interceptor, which synthesized pagination from
/// the returned array length.
/// </summary>
public static class ComsEndpoints
{
    public static void MapComsEndpoints(this IEndpointRouteBuilder app)
    {
        var coms = app.MapGroup("/v1/coms");
        coms.MapPost("/send/email", (Delegate)SendEmail);
        coms.MapPost("/send/sms", (Delegate)SendSms);
        coms.MapGet("/emails/batches", (Delegate)ListBatches);
        coms.MapGet("/emails/batches/{id}", (Delegate)GetBatch);
        coms.MapPost("/emails/batches/{id}/resend", (Delegate)ResendBatch);
        coms.MapGet("/emails/recipients", (Delegate)ListRecipients);
        coms.MapGet("/emails/recipients/{id}", (Delegate)GetRecipient);
        coms.MapPost("/emails/recipients/{id}/resend", (Delegate)ResendRecipient);
        coms.MapPost("/webhooks/{provider}", (Delegate)ReceiveWebhook);
    }

    private static async Task<IResult> SendEmail(HttpContext ctx, IDispatcher dispatcher, IJobQueue queue)
    {
        await RequestAuth.GateAsync(ctx);

        var body = await BodyValidator.ReadAsync(ctx);
        var fromEmail = body.OptionalEmail("fromEmail");
        var fromName = body.OptionalString("fromName");
        var toEmails = body.RequiredEmailArray("toEmails");
        var emailTemplate = body.RequiredString("emailTemplate");
        var data = body.RequiredObject("data");
        var documentIds = body.OptionalUuidArray("documentIds");
        body.RejectUnknownFields();
        body.ThrowIfInvalid();

        var result = await dispatcher.Send(
            new SendEmailCommand(fromEmail, fromName, toEmails!, emailTemplate, data!, documentIds),
            ctx.RequestAborted);
        // Enqueued HERE, after dispatch returns — TransactionBehavior has
        // committed the batch by now (SendEmailHandler's doc comment explains
        // why the handler itself must not enqueue). CancellationToken.None,
        // deliberately: this is post-commit work — the batch row already
        // exists — and must not be abandoned just because the client hung up
        // between the commit and this call. The frozen handler enqueued with
        // no cancellation token at all for the same reason.
        await queue.EnqueueAsync(
            new DispatchEmailBatchCommand(result.BatchId, RecipientId: null), CancellationToken.None);
        return ApiEnvelope.Success(ctx, StatusCodes.Status201Created, new JsonObject
        {
            ["batchId"] = result.BatchId.ToString(),
            ["jobId"] = result.JobId,
        });
    }

    private static async Task<IResult> SendSms(HttpContext ctx)
    {
        await RequestAuth.GateAsync(ctx);
        // The frozen sendSms() is a stub: 201, success envelope, no data key.
        return ApiEnvelope.Success(ctx, StatusCodes.Status201Created, includeData: false);
    }

    private static async Task<IResult> ListBatches(HttpContext ctx, IDispatcher dispatcher)
    {
        await RequestAuth.GateAsync(ctx);

        var query = QueryValidator.Read(ctx);
        var templateId = query.OptionalUuid("templateId");
        var overallStatus = query.OptionalEnum("overallStatus", EmailBatchStatus.All);
        var createdBy = query.OptionalUuid("createdBy");
        var fromDate = query.OptionalDateString("fromDate");
        var toDate = query.OptionalDateString("toDate");
        var page = query.OptionalInt("page", min: 1) ?? 1;
        var limit = query.OptionalInt("limit", min: 1, max: 100) ?? 20;
        query.RejectUnknownParams();
        query.ThrowIfInvalid();

        var batches = await dispatcher.Send(
            new ListEmailBatchesQuery(
                new BatchListFilter(templateId, overallStatus, createdBy, fromDate, toDate, page, limit)),
            ctx.RequestAborted);
        var data = new JsonArray([.. batches.Select(b => (JsonNode?)BatchJson(b, includeRecipients: false))]);
        return ApiEnvelope.Success(
            ctx, StatusCodes.Status200OK, data, extra: ApiEnvelope.SinglePagePagination(batches.Count));
    }

    private static async Task<IResult> GetBatch(HttpContext ctx, string id, IDispatcher dispatcher)
    {
        await RequestAuth.GateAsync(ctx);
        var batch = await dispatcher.Send(new GetEmailBatchQuery(Guid.Parse(id)), ctx.RequestAborted);
        return ApiEnvelope.Success(ctx, StatusCodes.Status200OK, BatchJson(batch, includeRecipients: true));
    }

    private static async Task<IResult> ResendBatch(
        HttpContext ctx, string id, IDispatcher dispatcher, IJobQueue queue)
    {
        await RequestAuth.GateAsync(ctx);
        var batchId = Guid.Parse(id);
        var jobId = await dispatcher.Send(new ResendEmailBatchCommand(batchId), ctx.RequestAborted);
        // Enqueued after dispatch returns — see SendEmail's comment; the same
        // commit-then-enqueue ordering applies here. CancellationToken.None:
        // post-commit work must not be abandoned on client disconnect.
        await queue.EnqueueAsync(
            new DispatchEmailBatchCommand(batchId, RecipientId: null), CancellationToken.None);
        return ApiEnvelope.Success(
            ctx, StatusCodes.Status201Created, new JsonObject { ["jobId"] = jobId });
    }

    private static async Task<IResult> ListRecipients(HttpContext ctx, IDispatcher dispatcher)
    {
        await RequestAuth.GateAsync(ctx);

        var query = QueryValidator.Read(ctx);
        var batchId = query.OptionalUuid("batchId");
        var toEmail = query.OptionalEmail("toEmail");
        var status = query.OptionalEnum("status", EmailRecipientStatus.All);
        var page = query.OptionalInt("page", min: 1) ?? 1;
        var limit = query.OptionalInt("limit", min: 1, max: 100) ?? 20;
        query.RejectUnknownParams();
        query.ThrowIfInvalid();

        var recipients = await dispatcher.Send(
            new ListEmailRecipientsQuery(new RecipientListFilter(batchId, toEmail, status, page, limit)),
            ctx.RequestAborted);
        var data = new JsonArray([.. recipients.Select(r => (JsonNode?)RecipientJson(r))]);
        return ApiEnvelope.Success(
            ctx, StatusCodes.Status200OK, data, extra: ApiEnvelope.SinglePagePagination(recipients.Count));
    }

    private static async Task<IResult> GetRecipient(
        HttpContext ctx, string id, IDispatcher dispatcher)
    {
        await RequestAuth.GateAsync(ctx);
        var recipient = await dispatcher.Send(new GetEmailRecipientQuery(Guid.Parse(id)), ctx.RequestAborted);
        return ApiEnvelope.Success(ctx, StatusCodes.Status200OK, RecipientJson(recipient));
    }

    private static async Task<IResult> ResendRecipient(
        HttpContext ctx, string id, IDispatcher dispatcher)
    {
        await RequestAuth.GateAsync(ctx);
        var jobId = await dispatcher.Send(new ResendEmailRecipientCommand(Guid.Parse(id)), ctx.RequestAborted);
        return ApiEnvelope.Success(
            ctx, StatusCodes.Status201Created, new JsonObject { ["jobId"] = jobId });
    }

    /// <summary>The frozen @Public provider webhook: no auth, raw body bytes.</summary>
    private static async Task<IResult> ReceiveWebhook(
        HttpContext ctx, string provider, IDispatcher dispatcher, IJobQueue queue)
    {
        using var buffer = new MemoryStream();
        await ctx.Request.Body.CopyToAsync(buffer);

        // Only the signature header matters to the handler (header lookup is
        // case-insensitive already) — no materialized header dictionary.
        string? signature = ctx.Request.Headers[MandrillSignatureVerifier.SignatureHeader];
        var result = await dispatcher.Send(
            new ReceiveWebhookCommand(provider, signature, buffer.ToArray()), ctx.RequestAborted);
        // Enqueued HERE, after dispatch returns — see ReceiveWebhookHandler's
        // doc comment: this is the fix for a real (not theoretical) race with
        // TransactionBehavior's commit-after-return semantics. CancellationToken.None,
        // deliberately: the event rows are already committed with
        // ProcessedAt == null: if the client disconnects between the commit
        // and this loop and we honored ctx.RequestAborted, the job would
        // never be queued, the provider's retry would dedup via
        // ExistingEventKeysAsync and return an empty EventIdsToProcess, and
        // the event would be permanently unprocessed — the exact
        // silent-forever failure this whole move was meant to fix, through a
        // narrower window. The frozen handler enqueued with no cancellation
        // token for the same reason.
        foreach (var eventId in result.EventIdsToProcess)
        {
            await queue.EnqueueAsync(new ProcessWebhookEventCommand(eventId), CancellationToken.None);
        }

        return ApiEnvelope.Success(
            ctx, StatusCodes.Status201Created, new JsonObject { ["received"] = result.Received });
    }

    /// <summary>The frozen EmailBatchEntity serialization (raw entity through the envelope).</summary>
    internal static JsonObject BatchJson(EmailBatch batch, bool includeRecipients)
    {
        var json = new JsonObject
        {
            ["id"] = batch.Id.ToString(),
            ["templateId"] = batch.TemplateId?.ToString(),
            ["fromEmail"] = batch.FromEmail,
            ["fromName"] = batch.FromName,
            ["data"] = JsonNode.Parse(batch.DataJson),
            ["documentIds"] = batch.DocumentIds is null
                ? null
                : new JsonArray([.. batch.DocumentIds.Select(d => (JsonNode?)d)]),
            ["jobId"] = batch.JobId,
            ["providerMessageId"] = batch.ProviderMessageId,
            ["overallStatus"] = batch.OverallStatus,
            ["createdBy"] = batch.CreatedBy?.ToString(),
            ["createdAt"] = IsoTimestamp.Of(batch.CreatedAt),
        };
        if (includeRecipients)
        {
            json["recipients"] = new JsonArray(
                [.. batch.Recipients.OrderBy(r => r.Id).Select(r => (JsonNode?)RecipientJson(r))]);
        }

        return json;
    }

    /// <summary>
    /// The frozen EmailRecipientEntity serialization — deliberately WITHOUT a
    /// batchId field (the frozen entity exposed only the unloaded relation).
    /// </summary>
    internal static JsonObject RecipientJson(EmailRecipient recipient) => new()
    {
        ["id"] = recipient.Id.ToString(),
        ["toEmail"] = recipient.ToEmail,
        ["messageId"] = recipient.MessageId,
        ["status"] = recipient.Status,
        ["sentAt"] = IsoTimestamp.Of(recipient.SentAt),
        ["errorMessage"] = recipient.ErrorMessage,
    };
}
