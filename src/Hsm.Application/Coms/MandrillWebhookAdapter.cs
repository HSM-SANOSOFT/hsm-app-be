using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hsm.Domain.Coms;

namespace Hsm.Application.Coms;

/// <summary>A provider event normalized to the common shape (frozen NormalizedWebhookEvent).</summary>
public sealed record NormalizedWebhookEvent(
    string EventType,
    string RecipientEmail,
    string? ProviderMessageId);

/// <summary>
/// The frozen Mandrill adapter (mandrill-webhook.adapter.ts): normalizes a
/// JSON array of Mandrill events into the common event shape.
/// </summary>
public static class MandrillWebhookAdapter
{
    /// <summary>The only provider the frozen factory supported (matched case-insensitively).</summary>
    public const string Provider = "mandrill";

    public static IReadOnlyList<NormalizedWebhookEvent> Normalize(JsonNode? rawPayload)
    {
        if (rawPayload is not JsonArray events)
        {
            return [];
        }

        return [.. events.Select(NormalizeEvent)];
    }

    private static NormalizedWebhookEvent NormalizeEvent(JsonNode? item)
    {
        var eventObject = item as JsonObject;
        var msg = eventObject?["msg"] as JsonObject;

        var messageId = StringOf(msg?["_id"]);
        return new NormalizedWebhookEvent(
            MapEventType(StringOf(eventObject?["event"]) ?? string.Empty),
            StringOf(msg?["email"]) ?? string.Empty,
            string.IsNullOrEmpty(messageId) ? null : messageId);
    }

    /// <summary>The frozen provider-event → common-event map.</summary>
    private static string MapEventType(string providerEvent) => providerEvent switch
    {
        "send" => EmailWebhookEventTypes.Delivered,
        "hard_bounce" => EmailWebhookEventTypes.BouncedHard,
        "soft_bounce" => EmailWebhookEventTypes.BouncedSoft,
        "spam" => EmailWebhookEventTypes.Spam,
        "reject" => EmailWebhookEventTypes.BouncedHard,
        "deferral" => EmailWebhookEventTypes.Deferred,
        "open" => EmailWebhookEventTypes.Open,
        "click" => EmailWebhookEventTypes.Click,
        "unsub" => EmailWebhookEventTypes.Unsubscribed,
        _ => EmailWebhookEventTypes.Unknown,
    };

    private static string? StringOf(JsonNode? node) => node?.GetValueKind() switch
    {
        JsonValueKind.String => node.GetValue<string>(),
        JsonValueKind.Number => node.AsValue().TryGetValue<double>(out var number)
            ? number.ToString(CultureInfo.InvariantCulture)
            : node.ToJsonString(),
        null => null,
        _ => node.ToJsonString(),
    };
}
