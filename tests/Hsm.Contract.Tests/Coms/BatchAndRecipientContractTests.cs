using System.Text.Json;

namespace Hsm.Contract.Tests.Coms;

/// <summary>
/// GET /v1/coms/emails/batches[/{id}] and recipients[/{id}] plus the two
/// resend routes — behavior pinned from the frozen coms.service: filters,
/// the interceptor's synthesized-pagination quirk, recipient JSON without a
/// batchId field, and resend semantics that REUSE rows (no duplicated batch
/// or recipients).
/// </summary>
public sealed class BatchAndRecipientContractTests(ComsApiFactory factory)
    : ComsContractTest(factory), IClassFixture<ComsApiFactory>
{
    [Fact]
    public async Task Batch_listing_filters_and_carries_the_frozen_pagination_quirk()
    {
        var (bearer, userId) = await BearerAsync();
        var templateName = await Factory.SeedEmailTemplateAsync();
        var first = await SendAsync(bearer, templateName);
        var second = await SendAsync(bearer, templateName);

        var mine = await Api.GetAsync(
            Client, $"/v1/coms/emails/batches?createdBy={userId}", bearer: bearer);
        AssertSuccessEnvelope(mine, 200, $"/v1/coms/emails/batches?createdBy={userId}");
        var ids = mine.Data.EnumerateArray().Select(b => b.GetProperty("id").GetString()).ToList();
        Assert.Contains(first, ids);
        Assert.Contains(second, ids);

        // The frozen quirk: limit paginates rows, but the envelope pagination
        // reflects only the returned page (the real total never surfaced).
        var limited = await Api.GetAsync(
            Client, $"/v1/coms/emails/batches?createdBy={userId}&limit=1", bearer: bearer);
        Assert.Equal(1, limited.Data.GetArrayLength());
        var pagination = limited.Metadata.GetProperty("extra").GetProperty("pagination");
        Assert.Equal(1, pagination.GetProperty("page").GetInt32());
        Assert.Equal(1, pagination.GetProperty("pageSize").GetInt32());
        Assert.Equal(1, pagination.GetProperty("totalItems").GetInt32());
        Assert.Equal(1, pagination.GetProperty("totalPages").GetInt32());

        var badStatus = await Api.GetAsync(
            Client, "/v1/coms/emails/batches?overallStatus=NOPE", bearer: bearer);
        AssertValidationFailure(badStatus, "overallStatus", "isEnum");

        var unknownParam = await Api.GetAsync(
            Client, "/v1/coms/emails/batches?sneaky=1", bearer: bearer);
        AssertValidationFailure(unknownParam, "sneaky", "whitelistValidation");
    }

    [Fact]
    public async Task Get_batch_includes_recipients_and_unknown_id_is_404()
    {
        var (bearer, _) = await BearerAsync();
        var templateName = await Factory.SeedEmailTemplateAsync();
        var batchId = await SendAsync(bearer, templateName);

        var batch = await GetBatchAsync(bearer, batchId);
        var recipient = batch.GetProperty("recipients")[0];
        Assert.False(recipient.TryGetProperty("batchId", out _));
        Assert.Equal(JsonValueKind.String, recipient.GetProperty("id").ValueKind);

        var missing = await Api.GetAsync(
            Client, $"/v1/coms/emails/batches/{Guid.NewGuid()}", bearer: bearer);
        AssertErrorEnvelope(missing, 404, "COMMON.NOT_FOUND");
    }

    [Fact]
    public async Task Recipient_listing_and_get_pin_the_frozen_shape()
    {
        var (bearer, _) = await BearerAsync();
        var templateName = await Factory.SeedEmailTemplateAsync();
        var address = $"{Unique("rcpt")}@dest.test";
        var batchId = await SendAsync(bearer, templateName, address);

        var byBatch = await Api.GetAsync(
            Client, $"/v1/coms/emails/recipients?batchId={batchId}", bearer: bearer);
        AssertSuccessEnvelope(byBatch, 200, $"/v1/coms/emails/recipients?batchId={batchId}");
        var listed = byBatch.Data.EnumerateArray().Single();
        Assert.Equal(address, listed.GetProperty("toEmail").GetString());
        Assert.False(listed.TryGetProperty("batchId", out _));

        var byEmail = await Api.GetAsync(
            Client, $"/v1/coms/emails/recipients?toEmail={address}", bearer: bearer);
        Assert.Single(byEmail.Data.EnumerateArray());

        var recipientId = listed.GetProperty("id").GetString();
        var single = await Api.GetAsync(
            Client, $"/v1/coms/emails/recipients/{recipientId}", bearer: bearer);
        AssertSuccessEnvelope(single, 200, $"/v1/coms/emails/recipients/{recipientId}");
        Assert.Equal(address, single.Data.GetProperty("toEmail").GetString());

        var missing = await Api.GetAsync(
            Client, $"/v1/coms/emails/recipients/{Guid.NewGuid()}", bearer: bearer);
        AssertErrorEnvelope(missing, 404, "COMMON.NOT_FOUND");
    }

    [Fact]
    public async Task Resending_a_failed_recipient_dispatches_again_without_duplicating_the_batch()
    {
        var (bearer, userId) = await BearerAsync();
        var templateName = await Factory.SeedEmailTemplateAsync();
        var address = $"{Unique("fail")}@dest.test";

        // Exhaust every retry so the recipient settles FAILED.
        Factory.Transport.FailNext(5);
        var batchId = await SendAsync(bearer, templateName, address);
        await WaitForAsync(async () =>
        {
            var current = await GetBatchAsync(bearer, batchId);
            return current.GetProperty("overallStatus").GetString() == "FAILED";
        }, "batch to settle FAILED");

        var failed = await GetBatchAsync(bearer, batchId);
        var failedRecipient = failed.GetProperty("recipients").EnumerateArray().Single();
        Assert.Equal("FAILED", failedRecipient.GetProperty("status").GetString());
        Assert.Equal(
            "Simulated SMTP failure", failedRecipient.GetProperty("errorMessage").GetString());
        var recipientId = failedRecipient.GetProperty("id").GetString();

        var batchesBefore = await CountBatchesAsync(userId);
        var resend = await Api.PostJsonAsync(
            Client, $"/v1/coms/emails/recipients/{recipientId}/resend", new { }, bearer: bearer);
        AssertSuccessEnvelope(resend, 201, $"/v1/coms/emails/recipients/{recipientId}/resend");
        Assert.False(string.IsNullOrEmpty(resend.Data.GetProperty("jobId").GetString()));

        await WaitForAsync(async () =>
        {
            var current = await GetBatchAsync(bearer, batchId);
            return current.GetProperty("overallStatus").GetString() == "SENT";
        }, "recipient resend to succeed");

        // Same batch, same recipient row — nothing duplicated.
        Assert.Equal(batchesBefore, await CountBatchesAsync(userId));
        var after = await GetBatchAsync(bearer, batchId);
        var recipients = after.GetProperty("recipients").EnumerateArray().ToList();
        var restored = Assert.Single(recipients);
        Assert.Equal(recipientId, restored.GetProperty("id").GetString());
        Assert.Equal("SENT", restored.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Batch_resend_retargets_only_pending_and_failed_rows()
    {
        var (bearer, _) = await BearerAsync();
        var templateName = await Factory.SeedEmailTemplateAsync();
        var address = $"{Unique("resend")}@dest.test";
        var batchId = await SendAsync(bearer, templateName, address);
        await WaitForAsync(async () =>
        {
            var current = await GetBatchAsync(bearer, batchId);
            return current.GetProperty("overallStatus").GetString() == "SENT";
        }, "initial dispatch");
        var sendsAfterFirst = SendsTo(address);
        Assert.Equal(1, sendsAfterFirst);

        var resend = await Api.PostJsonAsync(
            Client, $"/v1/coms/emails/batches/{batchId}/resend", new { }, bearer: bearer);
        AssertSuccessEnvelope(resend, 201, $"/v1/coms/emails/batches/{batchId}/resend");

        // The resend resets the batch (PENDING, then the dispatcher's
        // PROCESSING), but with every recipient already SENT it finds no
        // PENDING/FAILED target: no second delivery, rows reused untouched.
        await Task.Delay(300);
        var afterResend = await GetBatchAsync(bearer, batchId);
        Assert.Contains(
            afterResend.GetProperty("overallStatus").GetString(),
            new[] { "PENDING", "PROCESSING" });
        Assert.All(
            afterResend.GetProperty("recipients").EnumerateArray(),
            r => Assert.Equal("SENT", r.GetProperty("status").GetString()));
        Assert.Equal(sendsAfterFirst, SendsTo(address));

        var missing = await Api.PostJsonAsync(
            Client, $"/v1/coms/emails/batches/{Guid.NewGuid()}/resend", new { }, bearer: bearer);
        AssertErrorEnvelope(missing, 404, "COMMON.NOT_FOUND");
    }

    private async Task<string> SendAsync(string bearer, string templateName, string? address = null)
    {
        var response = await Api.PostJsonAsync(Client, "/v1/coms/send/email", new
        {
            toEmails = new[] { address ?? $"{Unique("to")}@dest.test" },
            emailTemplate = templateName,
            data = new { patientName = "Ada" },
        }, bearer: bearer);
        Assert.True(response.Status == 201, $"send failed: {response.RawBody}");
        return response.Data.GetProperty("batchId").GetString()!;
    }

    private int SendsTo(string address) =>
        Factory.Transport.Sent.Count(s => s.To.Contains(address, StringComparer.Ordinal));

    private Task<int> CountBatchesAsync(Guid createdBy) =>
        Factory.WithDbAsync(db => Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .CountAsync(db.EmailBatches.Where(b => b.CreatedBy == createdBy)));
}
