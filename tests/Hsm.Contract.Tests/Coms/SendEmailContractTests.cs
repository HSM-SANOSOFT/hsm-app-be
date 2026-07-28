using System.Text.Json;
using Hsm.Domain.Coms;
using Microsoft.EntityFrameworkCore;

namespace Hsm.Contract.Tests.Coms;

/// <summary>
/// POST /v1/coms/send/email and /send/sms — behavior pinned from the frozen
/// coms.controller/coms.service: template resolution (404), pre-dispatch
/// schema validation (400 before anything persists), the batch + recipient
/// aggregate, background dispatch to SENT, suppression skip, and the frozen
/// SMS stub.
/// </summary>
public sealed class SendEmailContractTests(ComsApiFactory factory)
    : ComsContractTest(factory), IClassFixture<ComsApiFactory>
{
    [Fact]
    public async Task Send_email_requires_authentication()
    {
        var response = await Api.PostJsonAsync(Client, "/v1/coms/send/email", new { });
        AssertErrorEnvelope(response, 401, "COMMON.UNAUTHORIZED");
    }

    [Fact]
    public async Task Send_email_creates_batch_with_recipients_and_dispatches_in_background()
    {
        var (bearer, userId) = await BearerAsync();
        var templateName = await Factory.SeedEmailTemplateAsync();

        var response = await Api.PostJsonAsync(Client, "/v1/coms/send/email", new
        {
            toEmails = new[] { $"{Unique("a")}@dest.test", $"{Unique("b")}@dest.test" },
            emailTemplate = templateName,
            data = new { patientName = "Ada" },
        }, bearer: bearer);

        AssertSuccessEnvelope(response, 201, "/v1/coms/send/email");
        var batchId = response.Data.GetProperty("batchId").GetString()!;
        Assert.False(string.IsNullOrEmpty(response.Data.GetProperty("jobId").GetString()));

        // The batch exists immediately, with per-recipient rows.
        var batch = await GetBatchAsync(bearer, batchId);
        Assert.Equal(userId.ToString(), batch.GetProperty("createdBy").GetString());
        Assert.Equal("Ada", batch.GetProperty("data").GetProperty("patientName").GetString());
        Assert.Equal(2, batch.GetProperty("recipients").GetArrayLength());

        // Background dispatch completes: recipients SENT with sentAt, batch
        // SENT with the provider message id.
        await WaitForAsync(async () =>
        {
            var current = await GetBatchAsync(bearer, batchId);
            return current.GetProperty("overallStatus").GetString() == "SENT";
        }, "batch to reach SENT");

        var sent = await GetBatchAsync(bearer, batchId);
        Assert.False(string.IsNullOrEmpty(sent.GetProperty("providerMessageId").GetString()));
        Assert.All(sent.GetProperty("recipients").EnumerateArray(), r =>
        {
            Assert.Equal("SENT", r.GetProperty("status").GetString());
            Assert.NotEqual(JsonValueKind.Null, r.GetProperty("sentAt").ValueKind);
        });

        // The transport saw ONE send carrying both recipients, rendered
        // through the base template ({{body}} inheritance) with the subject
        // compiled against the same data.
        var delivery = Factory.Transport.Sent.First(s =>
            s.To.Count == 2 && s.Html.Contains("Hola Ada", StringComparison.Ordinal));
        Assert.Equal("Hola Ada", delivery.Subject);
        Assert.Equal("<html><p>Hola Ada</p></html>", delivery.Html);

        // Dispatch rendering wrote a success parse log (frozen worker parse()).
        var logged = await Factory.WithDbAsync(db =>
            db.TemplateParseLogs.AnyAsync(l => l.Success && l.OutputLength > 0));
        Assert.True(logged);
    }

    [Fact]
    public async Task Send_email_with_unknown_template_is_404()
    {
        var (bearer, _) = await BearerAsync();

        var response = await Api.PostJsonAsync(Client, "/v1/coms/send/email", new
        {
            toEmails = new[] { "x@dest.test" },
            emailTemplate = "ghost_template",
            data = new { },
        }, bearer: bearer);

        var issue = AssertErrorEnvelope(response, 404, "COMMON.NOT_FOUND");
        Assert.Equal("Template 'ghost_template' not found", issue.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Send_email_with_missing_required_field_fails_before_dispatch()
    {
        var (bearer, userId) = await BearerAsync();
        var templateName = await Factory.SeedEmailTemplateAsync();
        var sendsBefore = Factory.Transport.Sent.Count;

        var response = await Api.PostJsonAsync(Client, "/v1/coms/send/email", new
        {
            toEmails = new[] { "x@dest.test" },
            emailTemplate = templateName,
            data = new { },
        }, bearer: bearer);

        var issue = AssertErrorEnvelope(response, 400, "COMMON.VALIDATION");
        Assert.Equal("Template data validation failed", issue.GetProperty("message").GetString());

        // Nothing was persisted and nothing dispatched.
        var created = await Factory.WithDbAsync(db =>
            db.EmailBatches.AnyAsync(b => b.CreatedBy == userId));
        Assert.False(created);
        Assert.Equal(sendsBefore, Factory.Transport.Sent.Count);
    }

    [Fact]
    public async Task Send_email_validates_the_frozen_dto_surface()
    {
        var (bearer, _) = await BearerAsync();

        var empty = await Api.PostJsonAsync(Client, "/v1/coms/send/email", new
        {
            toEmails = Array.Empty<string>(),
            emailTemplate = "t",
            data = new { },
        }, bearer: bearer);
        AssertValidationFailure(empty, "toEmails", "arrayNotEmpty");

        var badEmail = await Api.PostJsonAsync(Client, "/v1/coms/send/email", new
        {
            toEmails = new[] { "not-an-email" },
            emailTemplate = "t",
            data = new { },
        }, bearer: bearer);
        AssertValidationFailure(badEmail, "toEmails", "isEmail");

        var noData = await Api.PostJsonAsync(Client, "/v1/coms/send/email", new
        {
            toEmails = new[] { "x@dest.test" },
            emailTemplate = "t",
        }, bearer: bearer);
        AssertValidationFailure(noData, "data", "isObject");

        var unknown = await Api.PostJsonAsync(Client, "/v1/coms/send/email", new
        {
            toEmails = new[] { "x@dest.test" },
            emailTemplate = "t",
            data = new { },
            sneaky = 1,
        }, bearer: bearer);
        AssertValidationFailure(unknown, "sneaky", "whitelistValidation");
    }

    [Fact]
    public async Task Suppressed_addresses_are_skipped_not_dispatched()
    {
        var (bearer, _) = await BearerAsync();
        var templateName = await Factory.SeedEmailTemplateAsync();
        var suppressed = $"{Unique("supp")}@dest.test";
        var clean = $"{Unique("ok")}@dest.test";
        await Factory.WithDbAsync(async db =>
        {
            db.EmailSuppressions.Add(new EmailSuppression
            {
                Id = Guid.NewGuid(),
                Email = suppressed,
                Reason = EmailSuppressionReasons.Manual,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
            return true;
        });

        var response = await Api.PostJsonAsync(Client, "/v1/coms/send/email", new
        {
            toEmails = new[] { suppressed, clean },
            emailTemplate = templateName,
            data = new { patientName = "Ada" },
        }, bearer: bearer);
        AssertSuccessEnvelope(response, 201, "/v1/coms/send/email");
        var batchId = response.Data.GetProperty("batchId").GetString()!;

        await WaitForAsync(async () =>
        {
            var current = await GetBatchAsync(bearer, batchId);
            return current.GetProperty("overallStatus").GetString() == "PARTIAL";
        }, "batch to settle with the suppressed recipient skipped");

        var batch = await GetBatchAsync(bearer, batchId);
        var recipients = batch.GetProperty("recipients").EnumerateArray().ToList();
        Assert.Equal(
            "SUPPRESSED",
            recipients.First(r => r.GetProperty("toEmail").GetString() == suppressed)
                .GetProperty("status").GetString());
        Assert.Equal(
            "SENT",
            recipients.First(r => r.GetProperty("toEmail").GetString() == clean)
                .GetProperty("status").GetString());

        // The transport never saw the suppressed address.
        Assert.DoesNotContain(
            Factory.Transport.Sent, s => s.To.Contains(suppressed, StringComparer.Ordinal));
        Assert.Contains(Factory.Transport.Sent, s => s.To.Contains(clean, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Send_sms_is_the_frozen_stub_success_without_data()
    {
        var (bearer, _) = await BearerAsync();

        var response = await Api.PostJsonAsync(Client, "/v1/coms/send/sms", new { }, bearer: bearer);

        AssertSuccessEnvelope(response, 201, "/v1/coms/send/sms");
        Assert.False(response.HasData);
    }
}
