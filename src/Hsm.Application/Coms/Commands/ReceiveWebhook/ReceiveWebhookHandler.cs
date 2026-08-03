using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hsm.Application.Abstractions;
using Hsm.Application.Coms;
using Hsm.Application.Errors;
using Hsm.Application.Settings;
using Hsm.Domain.Coms;

namespace Hsm.Application.Coms.Commands.ReceiveWebhook;

/// <summary>
/// <b>Does not enqueue the process-webhook-event job itself</b> — unlike the
/// frozen handler and the pre-slicing port, which enqueued right after
/// <c>SaveChangesAsync</c>. <c>TransactionBehavior</c> now wraps this whole
/// handler in one transaction that commits only after <c>HandleAsync</c>
/// returns, so enqueuing in here raced the commit in practice, not just in
/// theory: <c>WebhookContractTests.Valid_webhook_updates_the_matching_recipient_delivery_state</c>
/// and <c>.Hard_bounce_suppresses_the_address_and_later_sends_skip_it</c>
/// failed on first run (the in-process channel consumer, in its own
/// scope/connection, looked up the new <c>EmailWebhookEvent</c> row before
/// this transaction committed it, found nothing, and — because "unknown
/// event" is a legitimate idempotent no-op here, unlike the send-email job's
/// "batch not found", which throws and gets a self-healing retry — silently
/// did nothing forever). <see cref="Hsm.Web.Coms.ComsEndpoints.ReceiveWebhook"/>
/// enqueues each returned id after <c>dispatcher.Send</c> returns, which is a
/// real post-commit point.
/// </summary>
public sealed class ReceiveWebhookHandler(
    IEmailWebhookEventStore events,
    IAppSettingStore settings,
    ISettingSeedSource seeds,
    Auth.IAuthUnitOfWork unitOfWork) : IRequestHandler<ReceiveWebhookCommand, ReceiveWebhookResult>
{
    public const string SigningKeysSettingKey = "COMS_WEBHOOK_SIGNING_KEYS";

    public async Task<ReceiveWebhookResult> HandleAsync(ReceiveWebhookCommand request, CancellationToken ct)
    {
        if (!string.Equals(request.Provider, MandrillWebhookAdapter.Provider, StringComparison.OrdinalIgnoreCase))
        {
            // Frozen: unknown providers are acknowledged with received: 0.
            return new ReceiveWebhookResult(0, []);
        }

        var signingKey = await SigningKeyForAsync(request.Provider, ct)
            ?? throw ApiException.BadRequest($"No signing key configured for provider: {request.Provider}");

        if (!MandrillSignatureVerifier.Verify(request.Signature, request.RawBody, signingKey))
        {
            // Frozen UnauthorizedException('Webhook signature invalid'):
            // rejected before ANYTHING is parsed, persisted, or enqueued.
            throw new ApiException(401, "Webhook signature invalid", errorLabel: "Unauthorized");
        }

        JsonNode? payload;
        try
        {
            payload = JsonNode.Parse(request.RawBody);
        }
        catch (JsonException)
        {
            throw ApiException.BadRequest("Invalid webhook body: not valid JSON");
        }

        var normalized = MandrillWebhookAdapter.Normalize(payload);
        if (normalized.Count == 0)
        {
            return new ReceiveWebhookResult(0, []);
        }

        var rawJson = Encoding.UTF8.GetString(request.RawBody);

        // One SELECT covers the whole delivery's idempotency check; the same
        // set then absorbs in-payload duplicates.
        var seen = (await events.ExistingEventKeysAsync(
                MandrillWebhookAdapter.Provider,
                [.. normalized.Where(i => i.ProviderMessageId is not null).Select(i => i.ProviderMessageId!)],
                ct))
            .ToHashSet();

        var added = new List<EmailWebhookEvent>();
        foreach (var item in normalized)
        {
            if (item.ProviderMessageId is not null && !seen.Add((item.ProviderMessageId, item.EventType)))
            {
                // Idempotent duplicate delivery: nothing recorded, nothing re-run.
                continue;
            }

            var entity = new EmailWebhookEvent
            {
                Id = Guid.NewGuid(),
                Provider = MandrillWebhookAdapter.Provider,
                EventType = item.EventType,
                RawPayloadJson = rawJson,
                RecipientEmail = item.RecipientEmail,
                MessageId = item.ProviderMessageId,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            await events.AddAsync(entity, ct);
            added.Add(entity);
        }

        if (added.Count == 0)
        {
            return new ReceiveWebhookResult(0, []);
        }

        // One SaveChanges for all new rows; the caller enqueues after commit.
        await unitOfWork.SaveChangesAsync(ct);

        return new ReceiveWebhookResult(added.Count, [.. added.Select(e => e.Id)]);
    }

    /// <summary>
    /// The frozen getWebhookSigningKeys(): the effective value of the secret
    /// COMS_WEBHOOK_SIGNING_KEYS setting (store row, else deploy seed) parsed
    /// as a JSON provider→key map; invalid JSON degrades to an empty map.
    /// </summary>
    private async Task<string?> SigningKeyForAsync(string provider, CancellationToken ct)
    {
        var rows = await settings.FindByKeysAsync([SigningKeysSettingKey], ct);
        var raw = rows.Count > 0 ? rows[0].Value : seeds.SeedValueFor(SigningKeysSettingKey);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            var map = JsonNode.Parse(raw) as JsonObject;
            var node = map?[provider.ToLowerInvariant()];
            return node?.GetValueKind() == JsonValueKind.String ? node.GetValue<string>() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
