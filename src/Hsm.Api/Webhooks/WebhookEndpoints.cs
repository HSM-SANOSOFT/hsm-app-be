using Hsm.Application.Abstractions;
using Hsm.Application.Coms;
using Hsm.Application.Coms.Commands.ProcessWebhookEvent;
using Hsm.Application.Coms.Commands.ReceiveWebhook;
using Hsm.Application.Ports;

namespace Hsm.Api.Webhooks;

/// <summary>
/// The provider webhook, moved across from the frozen coms-webhook.controller
/// (via <c>Hsm.Api.Coms.ComsEndpoints</c>) unchanged: raw body bytes,
/// HMAC-SHA1 verification, per-event enqueueing. <c>ReceiveWebhookCommand</c>
/// carries <see cref="Hsm.Application.Abstractions.AllowAnonymousRequestAttribute"/>
/// — the frozen route is <c>@Public</c>, the signature IS the credential — so
/// no endpoint-level auth call belongs here either.
/// </summary>
public static class WebhookEndpoints
{
    public static void MapWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.MapPost("/api/v1/webhooks/{provider}", ReceiveWebhook)
            .WithTags("Webhooks")
            .WithSummary("Receive a provider email delivery-status webhook.")
            .Produces<WebhookAcceptedResponse>(StatusCodes.Status202Accepted);
    }

    /// <summary>
    /// Do not "tidy" this into a JSON-bound parameter: the HMAC signature is
    /// computed over the exact raw request-body bytes, and binding through
    /// System.Text.Json would not preserve that.
    /// </summary>
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
        // narrower window.
        foreach (var eventId in result.EventIdsToProcess)
        {
            await queue.EnqueueAsync(new ProcessWebhookEventCommand(eventId), CancellationToken.None);
        }

        return Results.Accepted(uri: null, new WebhookAcceptedResponse(result.Received));
    }
}

public sealed record WebhookAcceptedResponse(int Received);
