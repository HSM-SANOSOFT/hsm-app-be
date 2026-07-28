using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Hsm.Contract.Tests.Coms;

/// <summary>
/// POST /v1/coms/webhooks/{provider} — behavior pinned from the frozen
/// coms-webhook.controller/service and mandrill-webhook.adapter: public
/// route; HMAC-SHA1 of the raw body bytes with the provider signing key,
/// base64, compared timing-safe against x-mandrill-signature; a forged
/// signature is a 401 that records NOTHING; events normalize to the common
/// shape and update the matching recipient; hard bounces and spam populate
/// the suppression list; duplicate provider deliveries are idempotent.
/// </summary>
public sealed class WebhookContractTests(ComsApiFactory factory)
    : ComsContractTest(factory), IClassFixture<ComsApiFactory>
{
    [Fact]
    public async Task Forged_signature_is_rejected_and_records_nothing()
    {
        var address = $"{Unique("forged")}@dest.test";
        var body = MandrillBody("send", address, Unique("msg"));

        var response = await PostWebhookAsync(body, signature: "forged-signature-value");

        var issue = AssertErrorEnvelope(response, 401, "COMMON.UNAUTHORIZED");
        Assert.Equal("Webhook signature invalid", issue.GetProperty("message").GetString());
        var recorded = await Factory.WithDbAsync(db =>
            db.EmailWebhookEvents.AnyAsync(e => e.RecipientEmail == address));
        Assert.False(recorded);
    }

    [Fact]
    public async Task Missing_signature_header_is_rejected()
    {
        var body = MandrillBody("send", $"{Unique("nosig")}@dest.test", Unique("msg"));
        var response = await PostWebhookAsync(body, signature: null);
        AssertErrorEnvelope(response, 401, "COMMON.UNAUTHORIZED");
    }

    [Fact]
    public async Task Valid_webhook_updates_the_matching_recipient_delivery_state()
    {
        var (bearer, _) = await BearerAsync();
        var templateName = await Factory.SeedEmailTemplateAsync();
        var address = $"{Unique("deliver")}@dest.test";
        var batchId = await SendAndSettleAsync(bearer, templateName, address);

        var body = MandrillBody("send", address, Unique("msg"));
        var response = await PostWebhookAsync(body, Sign(body));

        AssertSuccessEnvelope(response, 201, "/v1/coms/webhooks/mandrill");
        Assert.Equal(1, response.Data.GetProperty("received").GetInt32());

        await WaitForAsync(async () =>
        {
            var batch = await GetBatchAsync(bearer, batchId);
            var recipient = batch.GetProperty("recipients").EnumerateArray().Single();
            return recipient.GetProperty("status").GetString() == "DELIVERED";
        }, "webhook to mark the recipient DELIVERED");

        // The event row is linked and stamped processed.
        var processed = await Factory.WithDbAsync(db =>
            db.EmailWebhookEvents.AnyAsync(e =>
                e.RecipientEmail == address && e.ProcessedAt != null && e.RecipientId != null));
        Assert.True(processed);
    }

    [Fact]
    public async Task Hard_bounce_suppresses_the_address_and_later_sends_skip_it()
    {
        var (bearer, _) = await BearerAsync();
        var templateName = await Factory.SeedEmailTemplateAsync();
        var address = $"{Unique("bounce")}@dest.test";
        var batchId = await SendAndSettleAsync(bearer, templateName, address);

        var body = MandrillBody("hard_bounce", address, Unique("msg"));
        var response = await PostWebhookAsync(body, Sign(body));
        AssertSuccessEnvelope(response, 201, "/v1/coms/webhooks/mandrill");

        await WaitForAsync(async () =>
        {
            var batch = await GetBatchAsync(bearer, batchId);
            var recipient = batch.GetProperty("recipients").EnumerateArray().Single();
            return recipient.GetProperty("status").GetString() == "BOUNCED_HARD";
        }, "webhook to mark the recipient BOUNCED_HARD");

        var suppression = await Factory.WithDbAsync(db =>
            db.EmailSuppressions.SingleAsync(s => s.Email == address));
        Assert.Equal("HARD_BOUNCE", suppression.Reason);
        Assert.NotNull(suppression.SourceWebhookEventId);

        // A later send to the bounced address is skipped, not dispatched.
        var resend = await Api.PostJsonAsync(Client, "/v1/coms/send/email", new
        {
            toEmails = new[] { address },
            emailTemplate = templateName,
            data = new { patientName = "Ada" },
        }, bearer: bearer);
        var newBatchId = resend.Data.GetProperty("batchId").GetString()!;
        var newBatch = await GetBatchAsync(bearer, newBatchId);
        Assert.Equal(
            "SUPPRESSED",
            newBatch.GetProperty("recipients").EnumerateArray().Single()
                .GetProperty("status").GetString());
    }

    [Fact]
    public async Task Duplicate_webhook_events_for_the_same_provider_message_are_idempotent()
    {
        var (bearer, _) = await BearerAsync();
        var templateName = await Factory.SeedEmailTemplateAsync();
        var address = $"{Unique("dup")}@dest.test";
        var batchId = await SendAndSettleAsync(bearer, templateName, address);
        var messageId = Unique("msg");

        var body = MandrillBody("send", address, messageId);
        var first = await PostWebhookAsync(body, Sign(body));
        Assert.Equal(1, first.Data.GetProperty("received").GetInt32());

        await WaitForAsync(async () =>
        {
            var batch = await GetBatchAsync(bearer, batchId);
            var recipient = batch.GetProperty("recipients").EnumerateArray().Single();
            return recipient.GetProperty("status").GetString() == "DELIVERED";
        }, "first webhook processed");

        var second = await PostWebhookAsync(body, Sign(body));
        AssertSuccessEnvelope(second, 201, "/v1/coms/webhooks/mandrill");
        Assert.Equal(0, second.Data.GetProperty("received").GetInt32());

        var eventCount = await Factory.WithDbAsync(db =>
            db.EmailWebhookEvents.CountAsync(e => e.MessageId == messageId));
        Assert.Equal(1, eventCount);
        var batchAfter = await GetBatchAsync(bearer, batchId);
        Assert.Equal(
            "DELIVERED",
            batchAfter.GetProperty("recipients").EnumerateArray().Single()
                .GetProperty("status").GetString());
    }

    [Fact]
    public async Task Unknown_provider_is_acknowledged_with_zero_received()
    {
        var body = MandrillBody("send", $"{Unique("x")}@dest.test", Unique("msg"));
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/coms/webhooks/sendgrid")
        {
            Content = content,
        };
        var response = await ApiResponse.FromAsync(await Client.SendAsync(request));

        AssertSuccessEnvelope(response, 201, "/v1/coms/webhooks/sendgrid");
        Assert.Equal(0, response.Data.GetProperty("received").GetInt32());
    }

    [Fact]
    public async Task Invalid_json_body_with_valid_signature_is_400()
    {
        var body = "this is not json";
        var response = await PostWebhookAsync(body, Sign(body));
        var issue = AssertErrorEnvelope(response, 400, "COMMON.VALIDATION");
        Assert.Equal("Invalid webhook body: not valid JSON", issue.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Non_array_payload_yields_zero_received()
    {
        var body = """{"not":"an array"}""";
        var response = await PostWebhookAsync(body, Sign(body));
        AssertSuccessEnvelope(response, 201, "/v1/coms/webhooks/mandrill");
        Assert.Equal(0, response.Data.GetProperty("received").GetInt32());
    }

    /// <summary>The frozen signature scheme: HMAC-SHA1(rawBody) with the signing key, base64.</summary>
    private static string Sign(string body)
    {
        // CA5350: HMAC-SHA1 is the frozen Mandrill contract's algorithm — the
        // provider dictates it; changing it here would break real webhooks.
#pragma warning disable CA5350
        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(ComsApiFactory.SigningKey));
#pragma warning restore CA5350
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(body)));
    }

    private static string MandrillBody(string eventName, string address, string messageId) =>
        "[{\"event\":\"" + eventName + "\",\"ts\":1719878400,\"msg\":{\"email\":\"" + address
        + "\",\"_id\":\"" + messageId + "\",\"diag\":\"smtp;250 ok\"}}]";

    private async Task<ApiResponse> PostWebhookAsync(string body, string? signature)
    {
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/coms/webhooks/mandrill")
        {
            Content = content,
        };
        if (signature is not null)
        {
            request.Headers.Add("x-mandrill-signature", signature);
        }

        return await ApiResponse.FromAsync(await Client.SendAsync(request));
    }

    private async Task<string> SendAndSettleAsync(string bearer, string templateName, string address)
    {
        var response = await Api.PostJsonAsync(Client, "/v1/coms/send/email", new
        {
            toEmails = new[] { address },
            emailTemplate = templateName,
            data = new { patientName = "Ada" },
        }, bearer: bearer);
        Assert.True(response.Status == 201, $"send failed: {response.RawBody}");
        var batchId = response.Data.GetProperty("batchId").GetString()!;
        await WaitForAsync(async () =>
        {
            var batch = await GetBatchAsync(bearer, batchId);
            return batch.GetProperty("overallStatus").GetString() == "SENT";
        }, "initial dispatch to settle SENT");
        return batchId;
    }
}
