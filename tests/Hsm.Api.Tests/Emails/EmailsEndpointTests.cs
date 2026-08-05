using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Hsm.Application.Coms;
using Hsm.Domain.Coms;
using Hsm.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Hsm.Api.Tests.Emails;

public class EmailsEndpointTests(EmailsFactory factory) : IClassFixture<EmailsFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.EnsureSchemaAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static object SendBody(string template, string toEmail) => new
    {
        fromEmail = "noreply@api.test",
        fromName = "HSM",
        to = new[] { toEmail },
        emailTemplate = template,
        data = new { },
    };

    private static async Task<Guid> SendEmailAsync(HttpClient client, string template, string toEmail)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/emails", SendBody(template, toEmail), CancellationToken.None);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        return body.GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Sending_an_email_returns_202_with_a_location_header_and_ids()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Nurse);
        var template = await factory.SeedTemplateAsync();

        var response = await client.PostAsJsonAsync(
            "/api/v1/emails", SendBody(template, "patient@api.test"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var accepted = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        var id = accepted.GetProperty("id").GetGuid();
        Assert.Equal($"/api/v1/emails/{id}", response.Headers.Location?.ToString());
        Assert.False(string.IsNullOrWhiteSpace(accepted.GetProperty("jobId").GetString()));
    }

    [Fact]
    public async Task Getting_an_email_by_id_returns_the_detail_with_its_recipients()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Nurse);
        var template = await factory.SeedTemplateAsync();
        var id = await SendEmailAsync(client, template, "detail@api.test");

        // Re-read via a second call rather than trusting the send response —
        // proves the batch and its recipient row actually persisted.
        var response = await client.GetAsync($"/api/v1/emails/{id}", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        Assert.Equal(id, detail.GetProperty("id").GetGuid());
        Assert.Equal(1, detail.GetProperty("totalRecipients").GetInt32());
        var recipients = detail.GetProperty("recipients");
        Assert.Equal(1, recipients.GetArrayLength());
        Assert.Equal("detail@api.test", recipients[0].GetProperty("toEmail").GetString());
        // The resource field is templateId (Guid?), not emailTemplate — that
        // name is reserved for SendEmailRequest's human-readable identifier;
        // reusing it here would mean one field means two different things
        // depending on which direction you're reading it.
        Assert.True(detail.TryGetProperty("templateId", out _));
        Assert.False(detail.TryGetProperty("emailTemplate", out _));
    }

    [Fact]
    public async Task Sent_count_also_counts_delivered_recipients()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Nurse);
        var template = await factory.SeedTemplateAsync();
        var id = await SendEmailAsync(client, template, "delivered@api.test");

        // DispatchEmailBatchHandler's own rule (ComputeOverallStatus): SENT
        // and DELIVERED both count as sent — a recipient moves SENT ->
        // DELIVERED when the provider's webhook lands, which must not look
        // like a regression back to "not sent" on the very next read.
        await factory.WithDbAsync(async db =>
        {
            var recipient = await db.EmailRecipients.SingleAsync(r => r.BatchId == id);
            recipient.Status = EmailRecipientStatus.Delivered;
            await db.SaveChangesAsync();
            return true;
        });

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/v1/emails/{id}", CancellationToken.None);

        Assert.Equal(1, detail.GetProperty("sentCount").GetInt32());
    }

    [Fact]
    public async Task List_is_paged_and_reports_its_totals()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Nurse);
        var template = await factory.SeedTemplateAsync();
        await SendEmailAsync(client, template, "one@api.test");
        var newestId = await SendEmailAsync(client, template, "two@api.test");

        var response = await client.GetAsync("/api/v1/emails?page=1&pageSize=1", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        Assert.Equal(1, page.GetProperty("page").GetInt32());
        Assert.Equal(1, page.GetProperty("pageSize").GetInt32());
        Assert.True(page.GetProperty("totalItems").GetInt32() >= 2);
        var items = page.GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        // Pins EmailBatchStore.ListEmailsAsync's recipient aggregation: the
        // list query does NOT load recipient rows (see EmailBatchSummary), so
        // if the SQL counts were ever dropped, totalRecipients would silently
        // report 0 for a batch that actually has one.
        var newest = items[0];
        Assert.Equal(newestId, newest.GetProperty("id").GetGuid());
        Assert.Equal(1, newest.GetProperty("totalRecipients").GetInt32());
    }

    [Fact]
    public async Task The_lists_recipient_counts_are_the_details_own_counts()
    {
        // The list computes TotalRecipients/SentCount/FailedCount as SQL
        // aggregates; the detail counts the recipient rows it loaded anyway.
        // Two statements of one rule, so this is what stops them drifting —
        // and a batch with several recipients in mixed states is the only shape
        // that can tell a correct aggregate from an accidental one (a
        // single-recipient batch would agree even if the status predicate were
        // wrong).
        using var client = await factory.AuthenticatedClientAsync(Roles.Nurse);
        var template = await factory.SeedTemplateAsync();
        var response = await client.PostAsJsonAsync(
            "/api/v1/emails",
            new
            {
                fromEmail = "noreply@api.test",
                fromName = "HSM",
                to = new[] { "a@api.test", "b@api.test", "c@api.test", "d@api.test" },
                emailTemplate = template,
                data = new { },
            },
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None))
            .GetProperty("id").GetGuid();

        // One SENT, one DELIVERED (both count as sent), one FAILED, one left
        // PENDING — so all three numbers are distinct and none of them equals
        // the row count by coincidence.
        await SetRecipientStatusesAsync(
            id,
            EmailRecipientStatus.Sent,
            EmailRecipientStatus.Delivered,
            EmailRecipientStatus.Failed);

        var listed = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/emails?pageSize=100", CancellationToken.None);
        var row = listed.GetProperty("items").EnumerateArray()
            .First(item => item.GetProperty("id").GetGuid() == id);
        var detail = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/emails/{id}", CancellationToken.None);

        Assert.Equal(4, row.GetProperty("totalRecipients").GetInt32());
        Assert.Equal(2, row.GetProperty("sentCount").GetInt32());
        Assert.Equal(1, row.GetProperty("failedCount").GetInt32());
        foreach (var field in new[] { "totalRecipients", "sentCount", "failedCount" })
        {
            Assert.Equal(detail.GetProperty(field).GetInt32(), row.GetProperty(field).GetInt32());
        }
    }

    /// <summary>
    /// Stamps the batch's recipients, in id order, with the given statuses —
    /// any beyond the list keep whatever they were created with.
    /// </summary>
    private Task<int> SetRecipientStatusesAsync(Guid batchId, params string[] statuses) =>
        factory.WithDbAsync(async db =>
        {
            var recipients = await db.EmailRecipients
                .Where(r => r.BatchId == batchId)
                .OrderBy(r => r.Id)
                .ToListAsync(CancellationToken.None);
            for (var i = 0; i < statuses.Length && i < recipients.Count; i++)
            {
                recipients[i].Status = statuses[i];
            }

            return await db.SaveChangesAsync(CancellationToken.None);
        });

    [Fact]
    public async Task Sending_an_email_with_no_recipients_is_a_field_validation_failure()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Nurse);
        var template = await factory.SeedTemplateAsync();

        var response = await client.PostAsJsonAsync(
            "/api/v1/emails",
            new
            {
                fromEmail = (string?)null,
                fromName = (string?)null,
                to = Array.Empty<string>(),
                emailTemplate = template,
                data = new { },
            },
            CancellationToken.None);

        var problem = await ProblemAssert.ProblemAsync(response, 400);
        Assert.True(problem.GetProperty("errors").TryGetProperty("toEmails", out _));
    }

    [Fact]
    public async Task An_unknown_email_id_is_a_404_problem()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Nurse);

        var response = await client.GetAsync($"/api/v1/emails/{Guid.NewGuid()}", CancellationToken.None);

        await ProblemAssert.ProblemAsync(response, 404);
    }

    [Fact]
    public async Task A_non_guid_id_is_an_unmatched_route()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Nurse);

        var response = await client.GetAsync("/api/v1/emails/not-a-guid", CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Resending_a_batch_returns_202_and_actually_resets_its_status_to_pending()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Nurse);
        var template = await factory.SeedTemplateAsync();
        var id = await SendEmailAsync(client, template, "resend@api.test");

        // Force a non-PENDING state first, so a later PENDING read proves the
        // resend actually mutated the row rather than just echoing a status
        // that never changed.
        await factory.WithDbAsync(async db =>
        {
            var batch = await db.EmailBatches.SingleAsync(b => b.Id == id);
            batch.OverallStatus = EmailBatchStatus.Failed;
            await db.SaveChangesAsync();
            return true;
        });

        var response = await client.PostAsync($"/api/v1/emails/{id}/resend", content: null, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("jobId").GetString()));

        var fetched = await client.GetAsync($"/api/v1/emails/{id}", CancellationToken.None);
        var detail = await fetched.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        Assert.Equal(EmailBatchStatus.Pending, detail.GetProperty("overallStatus").GetString());
    }

    [Fact]
    public async Task Resending_a_recipient_returns_202_with_a_job_id()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Nurse);
        var template = await factory.SeedTemplateAsync();
        var id = await SendEmailAsync(client, template, "recipient@api.test");
        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/v1/emails/{id}", CancellationToken.None);
        var recipientId = detail.GetProperty("recipients")[0].GetProperty("id").GetGuid();

        var response = await client.PostAsync(
            $"/api/v1/emails/{id}/recipients/{recipientId}/resend", content: null, CancellationToken.None);

        // ResendEmailRecipientHandler resets no status (its own doc comment:
        // "no status reset here" is the frozen behavior) — there is no
        // observable persisted state to re-read, so the accepted envelope
        // itself is the only assertable outcome here.
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("jobId").GetString()));
    }

    [Fact]
    public async Task Resending_a_recipient_under_a_different_batch_id_in_the_url_is_a_404_problem()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Nurse);
        var template = await factory.SeedTemplateAsync();
        var id = await SendEmailAsync(client, template, "containment@api.test");
        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/v1/emails/{id}", CancellationToken.None);
        var recipientId = detail.GetProperty("recipients")[0].GetProperty("id").GetGuid();

        // recipientId is real, but the URL claims it belongs to a batch it
        // does not — the endpoint must verify that containment itself.
        var response = await client.PostAsync(
            $"/api/v1/emails/{Guid.NewGuid()}/recipients/{recipientId}/resend",
            content: null,
            CancellationToken.None);

        await ProblemAssert.ProblemAsync(response, 404);
    }

    [Theory]
    [InlineData("GET", "/api/v1/emails")]
    [InlineData("POST", "/api/v1/emails")]
    [InlineData("GET", "/api/v1/emails/00000000-0000-0000-0000-000000000001")]
    [InlineData("POST", "/api/v1/emails/00000000-0000-0000-0000-000000000001/resend")]
    [InlineData(
        "POST",
        "/api/v1/emails/00000000-0000-0000-0000-000000000001/recipients/00000000-0000-0000-0000-000000000002/resend")]
    public async Task Anonymous_callers_get_401_on_every_emails_route(string method, string route)
    {
        using var client = factory.CreateApiClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), route)
        {
            Content = JsonContent.Create(new { }),
        };

        var response = await client.SendAsync(request, CancellationToken.None);

        await ProblemAssert.ProblemAsync(response, 401);
    }

    [Fact]
    public async Task A_correctly_signed_webhook_event_is_accepted_anonymously_and_persisted()
    {
        using var client = factory.CreateApiClient();
        var payload = """[{"event":"send","msg":{"_id":"msg-signed-1","email":"delivered@api.test"}}]""";
        var rawBody = Encoding.UTF8.GetBytes(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/mandrill")
        {
            Content = new ByteArrayContent(rawBody),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Add(MandrillSignatureVerifier.SignatureHeader, EmailsFactory.SignMandrill(rawBody));

        var response = await client.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        Assert.Equal(1, body.GetProperty("received").GetInt32());

        // Persistence, not just the echoed count: the normalized event row
        // must actually be in the database.
        var recorded = await factory.WithDbAsync(db =>
            db.EmailWebhookEvents.CountAsync(e => e.MessageId == "msg-signed-1"));
        Assert.Equal(1, recorded);
    }

    [Fact]
    public async Task An_unsigned_webhook_event_is_401_not_the_pipelines_401()
    {
        using var client = factory.CreateApiClient();
        var payload = """[{"event":"send","msg":{"_id":"msg-unsigned-1","email":"nobody@api.test"}}]""";
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/mandrill")
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes(payload)),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        // Deliberately no x-mandrill-signature header: AllowAnonymousRequest
        // lets this reach the handler at all (unlike every /api/v1/emails*
        // route above); the handler's own signature check is what refuses it.

        var response = await client.SendAsync(request, CancellationToken.None);

        await ProblemAssert.ProblemAsync(response, 401);
    }
}
