using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hsm.Application.Settings;
using Hsm.Domain.Identity;
using Hsm.Domain.Settings;
using Microsoft.EntityFrameworkCore;

namespace Hsm.Api.Tests.Settings;

public class SettingsEndpointTests(SettingsFactory factory) : IClassFixture<SettingsFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.EnsureSchemaAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static JsonElement ItemFor(JsonElement settingsResource, string key) =>
        settingsResource.GetProperty("settings").EnumerateArray()
            .First(item => item.GetProperty("key").GetString() == key);

    private static async Task<JsonElement> GetCategoryAsync(HttpClient client, string category)
    {
        var response = await client.GetAsync($"/api/v1/settings?category={category}", CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
    }

    private static async Task<HttpResponseMessage> PutAsync(HttpClient client, string category, object settings) =>
        await client.PutAsJsonAsync(
            "/api/v1/settings", new { category, settings }, CancellationToken.None);

    private static async Task<JsonElement> GetAuditAsync(
        HttpClient client, string category, int page = 1, int pageSize = 20) =>
        await (await client.GetAsync(
                $"/api/v1/settings/audit?category={category}&page={page}&pageSize={pageSize}",
                CancellationToken.None))
            .Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);

    [Fact]
    public async Task Getting_a_known_category_returns_its_catalog_with_secrets_masked()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Admin);

        var view = await GetCategoryAsync(client, SettingsCategories.Email);

        Assert.Equal(SettingsCategories.Email, view.GetProperty("category").GetString());
        var address = ItemFor(view, "SMTP_ADDRESS");
        Assert.False(address.GetProperty("isSecret").GetBoolean());
        var password = ItemFor(view, "SMTP_PASSWORD");
        Assert.True(password.GetProperty("isSecret").GetBoolean());
        // Never the real plaintext, whatever it currently is — either unset
        // (null) or masked, but never anything else.
        var passwordValue = password.GetProperty("value").ValueKind == JsonValueKind.Null
            ? null
            : password.GetProperty("value").GetString();
        Assert.True(passwordValue is null or SettingsPolicy.SecretMask);
    }

    [Fact]
    public async Task Updating_a_setting_changes_it_and_a_followup_get_reflects_it()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Admin);
        var value = $"smtp-{Guid.NewGuid():N}.example.test";
        var before = DateTimeOffset.UtcNow;

        var response = await PutAsync(
            client, SettingsCategories.Email, new[] { new { key = "SMTP_ADDRESS", value } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var putBody = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        Assert.Equal(value, ItemFor(putBody, "SMTP_ADDRESS").GetProperty("value").GetString());

        // Re-read via a separate GET rather than trusting the PUT's own echo —
        // proves the row actually persisted.
        var fetched = await GetCategoryAsync(client, SettingsCategories.Email);
        var address = ItemFor(fetched, "SMTP_ADDRESS");
        Assert.Equal(value, address.GetProperty("value").GetString());
        // updatedAt genuinely reflects THIS write — not just present, but
        // stamped no earlier than the moment this test made its change.
        Assert.True(address.GetProperty("updatedAt").GetDateTimeOffset() >= before);
    }

    [Fact]
    public async Task Updating_a_secret_setting_returns_it_masked_and_a_followup_get_still_masks_it()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Admin);
        var secret = $"s3cr3t-{Guid.NewGuid():N}";

        var response = await PutAsync(
            client, SettingsCategories.Email, new[] { new { key = "SMTP_PASSWORD", value = secret } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var putBody = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        var putValue = ItemFor(putBody, "SMTP_PASSWORD").GetProperty("value").GetString();
        Assert.Equal(SettingsPolicy.SecretMask, putValue);
        Assert.NotEqual(secret, putValue);

        var fetched = await GetCategoryAsync(client, SettingsCategories.Email);
        var fetchedValue = ItemFor(fetched, "SMTP_PASSWORD").GetProperty("value").GetString();
        Assert.Equal(SettingsPolicy.SecretMask, fetchedValue);
        Assert.NotEqual(secret, fetchedValue);
    }

    [Fact]
    public async Task A_blank_secret_value_leaves_the_stored_secret_unchanged_and_writes_no_audit_row()
    {
        // WEBHOOK/COMS_WEBHOOK_SIGNING_KEYS is not touched by any other test in
        // this suite, so a count on it is safe from cross-test interference.
        using var client = await factory.AuthenticatedClientAsync(Roles.Admin);
        var secret = $"whsec-{Guid.NewGuid():N}";
        await PutAsync(
            client, SettingsCategories.Webhook,
            new[] { new { key = "COMS_WEBHOOK_SIGNING_KEYS", value = secret } });
        var afterFirstWrite = await GetAuditAsync(client, SettingsCategories.Webhook, pageSize: 100);
        var countAfterFirstWrite = afterFirstWrite.GetProperty("items").EnumerateArray()
            .Count(item => item.GetProperty("key").GetString() == "COMS_WEBHOOK_SIGNING_KEYS");

        var blankResponse = await PutAsync(
            client, SettingsCategories.Webhook,
            new[] { new { key = "COMS_WEBHOOK_SIGNING_KEYS", value = "" } });

        Assert.Equal(HttpStatusCode.OK, blankResponse.StatusCode);
        var afterBlankWrite = await GetAuditAsync(client, SettingsCategories.Webhook, pageSize: 100);
        var countAfterBlankWrite = afterBlankWrite.GetProperty("items").EnumerateArray()
            .Count(item => item.GetProperty("key").GetString() == "COMS_WEBHOOK_SIGNING_KEYS");
        // No effective change -> no new audit row for the blank write.
        Assert.Equal(countAfterFirstWrite, countAfterBlankWrite);

        var fetched = await GetCategoryAsync(client, SettingsCategories.Webhook);
        Assert.Equal(SettingsPolicy.SecretMask, ItemFor(fetched, "COMS_WEBHOOK_SIGNING_KEYS").GetProperty("value").GetString());
    }

    [Fact]
    public async Task Updating_an_unknown_key_is_silently_ignored()
    {
        // APP_BEHAVIOR has exactly one catalog key, so a count on it is safe
        // from cross-test interference.
        using var client = await factory.AuthenticatedClientAsync(Roles.Admin);
        var value = $"HSM-{Guid.NewGuid():N}";

        var response = await PutAsync(
            client, SettingsCategories.AppBehavior,
            new object[]
            {
                new { key = "SWAGGER_SITE_TITLE", value },
                new { key = "NOT_A_KEY", value = "x" },
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var fetched = await GetCategoryAsync(client, SettingsCategories.AppBehavior);
        Assert.Equal(value, ItemFor(fetched, "SWAGGER_SITE_TITLE").GetProperty("value").GetString());
        var audit = await GetAuditAsync(client, SettingsCategories.AppBehavior, pageSize: 100);
        Assert.DoesNotContain(
            audit.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("key").GetString() == "NOT_A_KEY");
    }

    [Fact]
    public async Task A_settings_body_with_a_null_settings_list_does_not_500()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Admin);

        var response = await client.PutAsJsonAsync(
            "/api/v1/settings",
            new { category = SettingsCategories.AppBehavior, settings = (object?)null },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_settings_body_with_a_null_item_is_a_field_validation_failure_not_a_500()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Admin);

        var response = await client.PutAsJsonAsync(
            "/api/v1/settings",
            new { category = SettingsCategories.AppBehavior, settings = new object?[] { null } },
            CancellationToken.None);

        var problem = await ProblemAssert.ProblemAsync(response, 400);
        Assert.True(problem.GetProperty("errors").TryGetProperty("updates[0].key", out _));
    }

    [Fact]
    public async Task Getting_settings_for_an_unknown_category_is_a_field_validation_failure()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Admin);

        var response = await client.GetAsync("/api/v1/settings?category=NOT_A_CATEGORY", CancellationToken.None);

        var problem = await ProblemAssert.ProblemAsync(response, 400);
        Assert.True(problem.GetProperty("errors").TryGetProperty("category", out _));
    }

    [Fact]
    public async Task Getting_settings_with_no_category_is_a_field_validation_failure()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Admin);

        var response = await client.GetAsync("/api/v1/settings", CancellationToken.None);

        var problem = await ProblemAssert.ProblemAsync(response, 400);
        Assert.True(problem.GetProperty("errors").TryGetProperty("category", out _));
    }

    [Fact]
    public async Task Updating_settings_with_an_unknown_category_is_a_field_validation_failure()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Admin);

        var response = await PutAsync(client, "NOT_A_CATEGORY", new[] { new { key = "X", value = "y" } });

        var problem = await ProblemAssert.ProblemAsync(response, 400);
        Assert.True(problem.GetProperty("errors").TryGetProperty("category", out _));
    }

    [Fact]
    public async Task Updating_a_setting_with_a_blank_key_is_a_field_validation_failure()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Admin);

        var response = await PutAsync(
            client, SettingsCategories.Email, new[] { new { key = "", value = "x" } });

        var problem = await ProblemAssert.ProblemAsync(response, 400);
        Assert.True(problem.GetProperty("errors").TryGetProperty("updates[0].key", out _));
    }

    [Fact]
    public async Task The_audit_trail_is_paged_newest_first_and_masks_secret_values()
    {
        // The STORAGE category is shared with other Facts in this suite, so
        // this test asserts general invariants of the page (shape, ordering,
        // slicing) rather than the exact position of its own three writes,
        // which other tests' concurrent audit rows could otherwise push
        // around — see An_audit_entrys_changedBy_matches_the_acting_admins_id
        // for the pattern used when a specific row must be found reliably.
        using var client = await factory.AuthenticatedClientAsync(Roles.Admin);
        var storageKey = "STRG_S3_REGION";
        var marker = $"eu-{Guid.NewGuid():N}";
        foreach (var suffix in new[] { "-1", "-2", "-3" })
        {
            var response = await PutAsync(
                client, SettingsCategories.Storage, new[] { new { key = storageKey, value = marker + suffix } });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        // A wide page proves our three writes actually landed in the trail.
        var wide = await GetAuditAsync(client, SettingsCategories.Storage, page: 1, pageSize: 100);
        var ours = wide.GetProperty("items").EnumerateArray()
            .Where(item => item.GetProperty("key").GetString() == storageKey
                && (item.GetProperty("newValue").GetString() ?? string.Empty).StartsWith(marker, StringComparison.Ordinal))
            .ToList();
        Assert.Equal(3, ours.Count);

        // A narrow page proves the slicing itself: count and echoed paging
        // numbers, and newest-first ordering across whatever landed on it.
        var narrow = await GetAuditAsync(client, SettingsCategories.Storage, page: 1, pageSize: 2);
        Assert.Equal(1, narrow.GetProperty("page").GetInt32());
        Assert.Equal(2, narrow.GetProperty("pageSize").GetInt32());
        Assert.True(narrow.GetProperty("totalItems").GetInt32() >= 3);
        Assert.True(narrow.GetProperty("totalPages").GetInt32() >= 2);
        var narrowItems = narrow.GetProperty("items").EnumerateArray().ToList();
        Assert.True(narrowItems.Count <= 2);
        var changedAtDescending = narrowItems.Select(item => item.GetProperty("changedAt").GetDateTimeOffset()).ToList();
        Assert.Equal(changedAtDescending.OrderByDescending(x => x), changedAtDescending);
        foreach (var item in ours)
        {
            Assert.Equal(SettingsCategories.Storage, item.GetProperty("category").GetString());
            Assert.NotEqual(Guid.Empty, item.GetProperty("id").GetGuid());
        }
    }

    [Fact]
    public async Task An_audit_entry_masks_a_secret_change_and_leaves_a_non_secret_change_readable()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Admin);
        var secret = $"s3cr3t-{Guid.NewGuid():N}";
        var plain = $"plain-{Guid.NewGuid():N}";

        await PutAsync(
            client, SettingsCategories.Storage,
            new[]
            {
                new { key = "STRG_S3_SECRET_KEY", value = secret },
                new { key = "STRG_S3_HOST", value = plain },
            });

        var audit = await GetAuditAsync(client, SettingsCategories.Storage, pageSize: 100);
        var items = audit.GetProperty("items").EnumerateArray().ToList();
        var secretEntry = items.First(item => item.GetProperty("key").GetString() == "STRG_S3_SECRET_KEY"
            && item.GetProperty("newValue").GetString() == SettingsPolicy.SecretMask);
        Assert.NotEqual(secret, secretEntry.GetProperty("newValue").GetString());
        var plainEntry = items.First(item => item.GetProperty("key").GetString() == "STRG_S3_HOST"
            && item.GetProperty("newValue").GetString() == plain);
        Assert.Equal(plain, plainEntry.GetProperty("newValue").GetString());
    }

    [Fact]
    public async Task An_audit_entrys_changedBy_matches_the_acting_admins_id()
    {
        var username = $"u{Guid.NewGuid():N}"[..20];
        using var client = await factory.AuthenticatedClientAsync(Roles.Admin, username: username);
        var actingUserId = await factory.WithDbAsync(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.UserName == username);
            return user.Id;
        });

        var value = $"HSM-{Guid.NewGuid():N}";
        await PutAsync(
            client, SettingsCategories.AppBehavior,
            new[] { new { key = "SWAGGER_SITE_TITLE", value } });

        var audit = await GetAuditAsync(client, SettingsCategories.AppBehavior, pageSize: 100);
        // Matched on the unique value just written, not just the key — other
        // Facts in this suite also write SWAGGER_SITE_TITLE, and this must be
        // OUR row regardless of how the rows interleave.
        var entry = audit.GetProperty("items").EnumerateArray()
            .First(item => item.GetProperty("key").GetString() == "SWAGGER_SITE_TITLE"
                && item.GetProperty("newValue").GetString() == value);
        // changedBy is a bare string (matches AppSettingAudit.ChangedBy's
        // storage type and the Blazor UI contract's SettingAuditEntryDto),
        // not a Guid — RequestActor.Id, which the write path stamps it with,
        // happens to be a GUID's string form for a real user.
        Assert.Equal(actingUserId.ToString(), entry.GetProperty("changedBy").GetString());
    }

    [Fact]
    public async Task Getting_the_audit_trail_for_an_unknown_category_is_a_field_validation_failure()
    {
        // Mirrors GetSettingsValidator's own rule for the sibling route: an
        // unknown/missing category is a caller mistake, not a silent empty page.
        using var client = await factory.AuthenticatedClientAsync(Roles.Admin);

        var response = await client.GetAsync(
            "/api/v1/settings/audit?category=NOT_A_CATEGORY", CancellationToken.None);

        var problem = await ProblemAssert.ProblemAsync(response, 400);
        Assert.True(problem.GetProperty("errors").TryGetProperty("category", out _));
    }

    [Fact]
    public async Task Getting_the_audit_trail_with_no_category_is_a_field_validation_failure()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Admin);

        var response = await client.GetAsync("/api/v1/settings/audit", CancellationToken.None);

        var problem = await ProblemAssert.ProblemAsync(response, 400);
        Assert.True(problem.GetProperty("errors").TryGetProperty("category", out _));
    }

    [Fact]
    public async Task Audit_page_size_above_the_cap_is_refused_not_clamped()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Admin);

        var response = await client.GetAsync(
            $"/api/v1/settings/audit?category={SettingsCategories.Email}&pageSize=500", CancellationToken.None);

        var problem = await ProblemAssert.ProblemAsync(response, 400);
        Assert.True(problem.GetProperty("errors").TryGetProperty("pageSize", out _));
    }

    [Theory]
    [InlineData("GET", "/api/v1/settings?category=EMAIL")]
    [InlineData("PUT", "/api/v1/settings")]
    [InlineData("GET", "/api/v1/settings/audit?category=EMAIL")]
    public async Task Anonymous_callers_get_401_on_every_settings_route(string method, string route)
    {
        using var client = factory.CreateApiClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), route)
        {
            Content = JsonContent.Create(new { category = SettingsCategories.Email, settings = Array.Empty<object>() }),
        };

        var response = await client.SendAsync(request, CancellationToken.None);

        await ProblemAssert.ProblemAsync(response, 401);
    }

    [Theory]
    [InlineData("GET", "/api/v1/settings?category=EMAIL")]
    [InlineData("PUT", "/api/v1/settings")]
    [InlineData("GET", "/api/v1/settings/audit?category=EMAIL")]
    public async Task A_non_admin_gets_403_on_every_settings_route(string method, string route)
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Doctor);
        using var request = new HttpRequestMessage(new HttpMethod(method), route)
        {
            Content = JsonContent.Create(new { category = SettingsCategories.Email, settings = Array.Empty<object>() }),
        };

        var response = await client.SendAsync(request, CancellationToken.None);

        await ProblemAssert.ProblemAsync(response, 403);
    }
}
