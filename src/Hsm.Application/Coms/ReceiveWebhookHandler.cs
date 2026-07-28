using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hsm.Application.Errors;
using Hsm.Application.Settings;
using Hsm.Domain.Coms;

namespace Hsm.Application.Coms;

/// <summary>
/// The frozen ComsWebhookService.receiveWebhook: adapter lookup (unknown
/// provider → received 0, no error), signing-key resolution from the settings
/// store (COMS_WEBHOOK_SIGNING_KEYS, a JSON provider→key map), signature
/// verification (401 on failure, recording NOTHING), JSON parse (400), then
/// normalize + persist + enqueue per event. Duplicate provider deliveries of
/// the same (provider, message, event type) are idempotent: they record no
/// second row and re-process nothing — a plan-mandated strengthening of the
/// frozen behavior, which deduped only same-row replays.
/// </summary>
public sealed class ReceiveWebhookHandler(
    IEmailWebhookEventStore events,
    IComsJobDispatcher queue,
    IAppSettingStore settings,
    ISettingSeedSource seeds,
    Auth.IAuthUnitOfWork unitOfWork)
{
    public const string SigningKeysSettingKey = "COMS_WEBHOOK_SIGNING_KEYS";

    public async Task<int> HandleAsync(
        string provider,
        string? signature,
        byte[] rawBody,
        CancellationToken ct = default)
    {
        if (!string.Equals(provider, MandrillWebhookAdapter.Provider, StringComparison.OrdinalIgnoreCase))
        {
            // Frozen: unknown providers are acknowledged with received: 0.
            return 0;
        }

        var signingKey = await SigningKeyForAsync(provider, ct)
            ?? throw ApiException.BadRequest($"No signing key configured for provider: {provider}");

        if (!MandrillSignatureVerifier.Verify(signature, rawBody, signingKey))
        {
            // Frozen UnauthorizedException('Webhook signature invalid'):
            // rejected before ANYTHING is parsed, persisted, or enqueued.
            throw new ApiException(401, "Webhook signature invalid", errorLabel: "Unauthorized");
        }

        JsonNode? payload;
        try
        {
            payload = JsonNode.Parse(rawBody);
        }
        catch (JsonException)
        {
            throw ApiException.BadRequest("Invalid webhook body: not valid JSON");
        }

        var normalized = MandrillWebhookAdapter.Normalize(payload);
        if (normalized.Count == 0)
        {
            return 0;
        }

        var rawJson = Encoding.UTF8.GetString(rawBody);

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
            return 0;
        }

        // One SaveChanges for all new rows, then the enqueues.
        await unitOfWork.SaveChangesAsync(ct);
        foreach (var entity in added)
        {
            await queue.EnqueueProcessWebhookEventAsync(entity.Id, ct);
        }

        return added.Count;
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
