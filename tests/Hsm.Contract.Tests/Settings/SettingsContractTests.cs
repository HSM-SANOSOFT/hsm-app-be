using System.Text.Json;
using Hsm.Contract.Tests.Auth;
using Hsm.Contract.Tests.Users;
using Microsoft.EntityFrameworkCore;

namespace Hsm.Contract.Tests.Settings;

/// <summary>
/// GET/PUT /v1/settings — behavior pinned from the frozen settings.controller /
/// settings.service at freeze/typescript-2026-07-27: catalog-driven categories,
/// env-seeded fallbacks, secrets masked on read AND in the audit log, blank
/// secrets ignored, unknown keys ignored, every effective change audited with
/// actor + old/new values inside the same transaction.
/// </summary>
public sealed class SettingsContractTests(UsersApiFactory factory)
    : UsersContractTest(factory), IClassFixture<UsersApiFactory>
{
    private static readonly string[] EmailCatalogKeys =
        ["SMTP_ADDRESS", "SMTP_USERNAME", "SMTP_PASSWORD", "SMTP_PORT", "SMTP_SECURE"];

    [Fact]
    public async Task Admin_reads_a_category_with_env_seeds_and_masked_secrets()
    {
        var (bearer, _) = await AdminBearerAsync();

        var response = await Api.GetAsync(Client, "/v1/settings?category=EMAIL", bearer: bearer);

        AssertSuccessEnvelope(response, 200, "/v1/settings?category=EMAIL");
        Assert.Equal("EMAIL", response.Data.GetProperty("category").GetString());
        var items = response.Data.GetProperty("settings").EnumerateArray()
            .ToDictionary(i => i.GetProperty("key").GetString()!);

        // The frozen EMAIL catalog: 5 definitions.
        Assert.Equal(5, items.Count);
        Assert.All(EmailCatalogKeys, key => Assert.True(items.ContainsKey(key), $"missing catalog key {key}"));

        // Non-secret with an env seed: raw value, isSet.
        var address = items["SMTP_ADDRESS"];
        Assert.Equal(UsersApiFactory.SeededSmtpAddress, address.GetProperty("value").GetString());
        Assert.True(address.GetProperty("isSet").GetBoolean());
        Assert.False(address.GetProperty("isSecret").GetBoolean());
        Assert.Equal("EMAIL", address.GetProperty("category").GetString());

        // Secret with an env seed: masked, never the plaintext.
        var password = items["SMTP_PASSWORD"];
        Assert.True(password.GetProperty("isSecret").GetBoolean());
        Assert.True(password.GetProperty("isSet").GetBoolean());
        Assert.Equal("********", password.GetProperty("value").GetString());
        Assert.DoesNotContain(UsersApiFactory.SeededSmtpPassword, response.RawBody, StringComparison.Ordinal);

        // Unseeded, unset: null value, not set.
        var username = items["SMTP_USERNAME"];
        Assert.False(username.GetProperty("isSet").GetBoolean());
        Assert.Equal(JsonValueKind.Null, username.GetProperty("value").ValueKind);
    }

    [Fact]
    public async Task Updating_a_setting_writes_an_audit_row_with_actor_and_change()
    {
        var (bearer, adminId) = await AdminBearerAsync();

        var response = await Api.PutJsonAsync(
            Client, "/v1/settings", new
            {
                category = "EMAIL",
                settings = new[] { new { key = "SMTP_PORT", value = "2525" } },
            }, bearer: bearer);

        AssertSuccessEnvelope(response, 200, "/v1/settings");
        var items = response.Data.GetProperty("settings").EnumerateArray()
            .ToDictionary(i => i.GetProperty("key").GetString()!);
        Assert.Equal("2525", items["SMTP_PORT"].GetProperty("value").GetString());

        var audit = await Factory.WithDbAsync(db => db.AppSettingAudits
            .Where(a => a.Key == "SMTP_PORT")
            .OrderByDescending(a => a.ChangedAt)
            .FirstAsync());
        Assert.Equal(adminId.ToString(), audit.ChangedBy);
        Assert.Null(audit.OldValue); // no row and no env seed before this write
        Assert.Equal("2525", audit.NewValue);
        Assert.Equal("EMAIL", audit.Category);
    }

    [Fact]
    public async Task Overriding_an_env_seeded_value_audits_the_seed_as_the_old_value()
    {
        var (bearer, adminId) = await AdminBearerAsync();

        var response = await Api.PutJsonAsync(
            Client, "/v1/settings", new
            {
                category = "EMAIL",
                settings = new[] { new { key = "SMTP_ADDRESS", value = "smtp.updated.test" } },
            }, bearer: bearer);
        AssertSuccessEnvelope(response, 200, "/v1/settings");

        var audit = await Factory.WithDbAsync(db => db.AppSettingAudits
            .Where(a => a.Key == "SMTP_ADDRESS")
            .OrderByDescending(a => a.ChangedAt)
            .FirstAsync());
        Assert.Equal(adminId.ToString(), audit.ChangedBy);
        Assert.Equal(UsersApiFactory.SeededSmtpAddress, audit.OldValue);
        Assert.Equal("smtp.updated.test", audit.NewValue);

        // Read-back returns the stored override, not the seed.
        var read = await Api.GetAsync(Client, "/v1/settings?category=EMAIL", bearer: bearer);
        var address = read.Data.GetProperty("settings").EnumerateArray()
            .First(i => i.GetProperty("key").GetString() == "SMTP_ADDRESS");
        Assert.Equal("smtp.updated.test", address.GetProperty("value").GetString());
    }

    [Fact]
    public async Task Secret_updates_are_masked_in_the_audit_and_on_read()
    {
        var (bearer, adminId) = await AdminBearerAsync();

        var response = await Api.PutJsonAsync(
            Client, "/v1/settings", new
            {
                category = "STORAGE",
                settings = new[] { new { key = "STRG_S3_SECRET_KEY", value = "super-secret-value" } },
            }, bearer: bearer);
        AssertSuccessEnvelope(response, 200, "/v1/settings");
        // Neither the response nor the audit ever carries the plaintext.
        Assert.DoesNotContain("super-secret-value", response.RawBody, StringComparison.Ordinal);

        var audit = await Factory.WithDbAsync(db => db.AppSettingAudits
            .Where(a => a.Key == "STRG_S3_SECRET_KEY")
            .OrderByDescending(a => a.ChangedAt)
            .FirstAsync());
        Assert.Equal(adminId.ToString(), audit.ChangedBy);
        Assert.Null(audit.OldValue); // previously unset -> null, not a mask
        Assert.Equal("********", audit.NewValue);

        // Stored plaintext, masked on read, isSet true.
        var row = await Factory.WithDbAsync(db =>
            db.AppSettings.FirstAsync(s => s.Key == "STRG_S3_SECRET_KEY"));
        Assert.Equal("super-secret-value", row.Value);
        var read = await Api.GetAsync(Client, "/v1/settings?category=STORAGE", bearer: bearer);
        var item = read.Data.GetProperty("settings").EnumerateArray()
            .First(i => i.GetProperty("key").GetString() == "STRG_S3_SECRET_KEY");
        Assert.True(item.GetProperty("isSet").GetBoolean());
        Assert.Equal("********", item.GetProperty("value").GetString());
    }

    [Fact]
    public async Task Blank_secret_leaves_the_stored_value_unchanged_with_no_audit()
    {
        var (bearer, _) = await AdminBearerAsync();
        // Ensure a stored secret exists.
        await Api.PutJsonAsync(
            Client, "/v1/settings", new
            {
                category = "WEBHOOK",
                settings = new[] { new { key = "COMS_WEBHOOK_SIGNING_KEYS", value = "signing-key-1" } },
            }, bearer: bearer);
        var auditsBefore = await Factory.WithDbAsync(db =>
            db.AppSettingAudits.CountAsync(a => a.Key == "COMS_WEBHOOK_SIGNING_KEYS"));

        var response = await Api.PutJsonAsync(
            Client, "/v1/settings", new
            {
                category = "WEBHOOK",
                settings = new[] { new { key = "COMS_WEBHOOK_SIGNING_KEYS", value = "  " } },
            }, bearer: bearer);
        AssertSuccessEnvelope(response, 200, "/v1/settings");

        var row = await Factory.WithDbAsync(db =>
            db.AppSettings.FirstAsync(s => s.Key == "COMS_WEBHOOK_SIGNING_KEYS"));
        Assert.Equal("signing-key-1", row.Value);
        var auditsAfter = await Factory.WithDbAsync(db =>
            db.AppSettingAudits.CountAsync(a => a.Key == "COMS_WEBHOOK_SIGNING_KEYS"));
        Assert.Equal(auditsBefore, auditsAfter);
    }

    [Fact]
    public async Task Unknown_and_category_mismatched_keys_are_ignored()
    {
        var (bearer, _) = await AdminBearerAsync();

        var response = await Api.PutJsonAsync(
            Client, "/v1/settings", new
            {
                category = "EMAIL",
                settings = new[]
                {
                    new { key = "NOT_A_SETTING", value = "x" },
                    // STORAGE key under an EMAIL payload: category mismatch.
                    new { key = "STRG_S3_HOST", value = "http://sneaky" },
                },
            }, bearer: bearer);

        AssertSuccessEnvelope(response, 200, "/v1/settings");
        var unknownRows = await Factory.WithDbAsync(db =>
            db.AppSettings.CountAsync(s => s.Key == "NOT_A_SETTING" || s.Key == "STRG_S3_HOST"));
        Assert.Equal(0, unknownRows);
        var audits = await Factory.WithDbAsync(db =>
            db.AppSettingAudits.CountAsync(a => a.Key == "NOT_A_SETTING" || a.Key == "STRG_S3_HOST"));
        Assert.Equal(0, audits);
    }

    [Fact]
    public async Task Writing_the_unchanged_value_produces_no_audit_entry()
    {
        var (bearer, _) = await AdminBearerAsync();
        await Api.PutJsonAsync(
            Client, "/v1/settings", new
            {
                category = "EMAIL",
                settings = new[] { new { key = "SMTP_SECURE", value = "true" } },
            }, bearer: bearer);
        var auditsBefore = await Factory.WithDbAsync(db =>
            db.AppSettingAudits.CountAsync(a => a.Key == "SMTP_SECURE"));

        var response = await Api.PutJsonAsync(
            Client, "/v1/settings", new
            {
                category = "EMAIL",
                settings = new[] { new { key = "SMTP_SECURE", value = "true" } },
            }, bearer: bearer);

        AssertSuccessEnvelope(response, 200, "/v1/settings");
        var auditsAfter = await Factory.WithDbAsync(db =>
            db.AppSettingAudits.CountAsync(a => a.Key == "SMTP_SECURE"));
        Assert.Equal(auditsBefore, auditsAfter);
    }

    [Fact]
    public async Task Category_is_required_and_validated_on_read()
    {
        var (bearer, _) = await AdminBearerAsync();

        AssertValidationFailure(
            await Api.GetAsync(Client, "/v1/settings", bearer: bearer), "category", "isEnum");
        AssertValidationFailure(
            await Api.GetAsync(Client, "/v1/settings?category=NOPE", bearer: bearer), "category", "isEnum");
    }

    [Fact]
    public async Task Update_payload_requires_a_non_empty_settings_array_and_a_valid_category()
    {
        var (bearer, _) = await AdminBearerAsync();

        AssertValidationFailure(
            await Api.PutJsonAsync(
                Client, "/v1/settings", new { category = "EMAIL", settings = Array.Empty<object>() }, bearer: bearer),
            "settings", "arrayNotEmpty");
        AssertValidationFailure(
            await Api.PutJsonAsync(
                Client, "/v1/settings",
                new { category = "NOPE", settings = new[] { new { key = "SMTP_PORT", value = "1" } } },
                bearer: bearer),
            "category", "isEnum");
    }

    [Fact]
    public async Task Non_admin_access_is_403_for_both_operations()
    {
        var (username, password, _, _) = await SeedPatientAsync();
        var login = await LoginAsync(username, password);

        AssertErrorEnvelope(
            await Api.GetAsync(Client, "/v1/settings?category=EMAIL", bearer: login.AccessToken),
            403, "COMMON.FORBIDDEN");
        AssertErrorEnvelope(
            await Api.PutJsonAsync(
                Client, "/v1/settings",
                new { category = "EMAIL", settings = new[] { new { key = "SMTP_PORT", value = "1" } } },
                bearer: login.AccessToken),
            403, "COMMON.FORBIDDEN");
    }
}
