using System.Globalization;
using System.Text.Json.Nodes;
using Hsm.Application.Auth;
using Hsm.Application.Coms;
using Hsm.Domain.Coms;
using Hsm.Web.Api;
using Hsm.Web.Auth;
using Hsm.Web.Templates;

namespace Hsm.Web.Coms;

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

    private static async Task<IResult> SendEmail(HttpContext ctx, SendEmailHandler handler)
    {
        var principal = await AuthorizeAsync(ctx);

        var body = await BodyValidator.ReadAsync(ctx);
        var fromEmail = body.OptionalEmail("fromEmail");
        var fromName = body.OptionalString("fromName");
        var toEmails = body.RequiredEmailArray("toEmails");
        var emailTemplate = body.RequiredString("emailTemplate");
        var data = body.RequiredObject("data");
        var documentIds = body.OptionalUuidArray("documentIds");
        body.RejectUnknownFields();
        body.ThrowIfInvalid();

        var (batchId, jobId) = await handler.HandleAsync(
            new SendEmailHandler.Command(fromEmail, fromName, toEmails!, emailTemplate, data!, documentIds),
            Guid.Parse(principal.Id));
        return ApiEnvelope.Success(ctx, StatusCodes.Status201Created, new JsonObject
        {
            ["batchId"] = batchId.ToString(),
            ["jobId"] = jobId,
        });
    }

    private static async Task<IResult> SendSms(HttpContext ctx)
    {
        await AuthorizeAsync(ctx);
        // The frozen sendSms() is a stub: 201, success envelope, no data key.
        return ApiEnvelope.Success(ctx, StatusCodes.Status201Created, includeData: false);
    }

    private static async Task<IResult> ListBatches(HttpContext ctx, ListEmailBatchesHandler handler)
    {
        await AuthorizeAsync(ctx);

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

        var batches = await handler.HandleAsync(
            new BatchListFilter(templateId, overallStatus, createdBy, fromDate, toDate, page, limit));
        var data = new JsonArray([.. batches.Select(b => (JsonNode?)BatchJson(b, includeRecipients: false))]);
        return ApiEnvelope.Success(
            ctx, StatusCodes.Status200OK, data, extra: TemplateEndpoints.SyntheticPagination(batches.Count));
    }

    private static async Task<IResult> GetBatch(HttpContext ctx, string id, GetEmailBatchHandler handler)
    {
        await AuthorizeAsync(ctx);
        var batch = await handler.HandleAsync(Guid.Parse(id));
        return ApiEnvelope.Success(ctx, StatusCodes.Status200OK, BatchJson(batch, includeRecipients: true));
    }

    private static async Task<IResult> ResendBatch(HttpContext ctx, string id, ResendEmailBatchHandler handler)
    {
        await AuthorizeAsync(ctx);
        var jobId = await handler.HandleAsync(Guid.Parse(id));
        return ApiEnvelope.Success(
            ctx, StatusCodes.Status201Created, new JsonObject { ["jobId"] = jobId });
    }

    private static async Task<IResult> ListRecipients(HttpContext ctx, ListEmailRecipientsHandler handler)
    {
        await AuthorizeAsync(ctx);

        var query = QueryValidator.Read(ctx);
        var batchId = query.OptionalUuid("batchId");
        var toEmail = query.OptionalEmail("toEmail");
        var status = query.OptionalEnum("status", EmailRecipientStatus.All);
        var page = query.OptionalInt("page", min: 1) ?? 1;
        var limit = query.OptionalInt("limit", min: 1, max: 100) ?? 20;
        query.RejectUnknownParams();
        query.ThrowIfInvalid();

        var recipients = await handler.HandleAsync(
            new RecipientListFilter(batchId, toEmail, status, page, limit));
        var data = new JsonArray([.. recipients.Select(r => (JsonNode?)RecipientJson(r))]);
        return ApiEnvelope.Success(
            ctx, StatusCodes.Status200OK, data, extra: TemplateEndpoints.SyntheticPagination(recipients.Count));
    }

    private static async Task<IResult> GetRecipient(
        HttpContext ctx, string id, GetEmailRecipientHandler handler)
    {
        await AuthorizeAsync(ctx);
        var recipient = await handler.HandleAsync(Guid.Parse(id));
        return ApiEnvelope.Success(ctx, StatusCodes.Status200OK, RecipientJson(recipient));
    }

    private static async Task<IResult> ResendRecipient(
        HttpContext ctx, string id, ResendEmailRecipientHandler handler)
    {
        await AuthorizeAsync(ctx);
        var jobId = await handler.HandleAsync(Guid.Parse(id));
        return ApiEnvelope.Success(
            ctx, StatusCodes.Status201Created, new JsonObject { ["jobId"] = jobId });
    }

    /// <summary>The frozen @Public provider webhook: no auth, raw body bytes.</summary>
    private static async Task<IResult> ReceiveWebhook(
        HttpContext ctx, string provider, ReceiveWebhookHandler handler)
    {
        using var buffer = new MemoryStream();
        await ctx.Request.Body.CopyToAsync(buffer);

        var headers = ctx.Request.Headers.ToDictionary(
            h => h.Key.ToLowerInvariant(), h => h.Value.ToString(), StringComparer.Ordinal);
        var received = await handler.HandleAsync(provider, headers, buffer.ToArray());
        return ApiEnvelope.Success(
            ctx, StatusCodes.Status201Created, new JsonObject { ["received"] = received });
    }

    /// <summary>The frozen guard chain: authenticated, any role, onboarding complete.</summary>
    private static async Task<AuthPrincipal> AuthorizeAsync(HttpContext ctx)
    {
        var principal = await RequestAuth.AuthenticateAsync(ctx, TokenKind.Access);
        RequestAuth.RequireRoles(ctx, principal);
        await RequestAuth.RequireOnboardingCompletedAsync(ctx, principal);
        return principal;
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
            ["createdAt"] = Iso(batch.CreatedAt),
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
        ["sentAt"] = recipient.SentAt is null ? null : Iso(recipient.SentAt.Value),
        ["errorMessage"] = recipient.ErrorMessage,
    };

    private static string Iso(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
}
