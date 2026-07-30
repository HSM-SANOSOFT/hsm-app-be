using Hsm.Application.Abstractions;

namespace Hsm.Application.Coms.Commands.ReceiveWebhook;

/// <summary>
/// The frozen ComsWebhookService.receiveWebhook: adapter lookup (unknown
/// provider → received 0, no error), signing-key resolution from the settings
/// store (COMS_WEBHOOK_SIGNING_KEYS, a JSON provider→key map), signature
/// verification (401 on failure, recording NOTHING), JSON parse (400), then
/// normalize + persist + enqueue per event. Duplicate provider deliveries of
/// the same (provider, message, event type) are idempotent: they record no
/// second row and re-process nothing — a plan-mandated strengthening of the
/// frozen behavior, which deduped only same-row replays.
///
/// <see cref="AllowAnonymousRequestAttribute"/>: the frozen route is
/// <c>@Public</c> — the HMAC-SHA1 signature over the raw body IS the
/// credential, verified inside the handler exactly as strongly as before.
/// </summary>
[AllowAnonymousRequest]
public sealed record ReceiveWebhookCommand(
    string Provider, string? Signature, byte[] RawBody) : ICommand<ReceiveWebhookResult>;

/// <summary>
/// The frozen response is just <c>received</c> (the count); <see cref="EventIdsToProcess"/>
/// is NOT part of that wire shape — it exists so the caller can enqueue the
/// process-webhook-event job strictly AFTER this command's transaction
/// commits (see <see cref="ReceiveWebhookHandler"/>'s doc comment for why the
/// handler itself must not enqueue).
/// </summary>
public sealed record ReceiveWebhookResult(int Received, IReadOnlyList<Guid> EventIdsToProcess);
