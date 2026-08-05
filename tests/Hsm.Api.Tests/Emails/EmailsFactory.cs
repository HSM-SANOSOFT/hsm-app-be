using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Hsm.Domain.Templates;
using Microsoft.AspNetCore.Hosting;

namespace Hsm.Api.Tests.Emails;

public sealed class EmailsFactory : ApiFactory
{
    /// <summary>
    /// Matches the signing key seeded into COMS_WEBHOOK_SIGNING_KEYS below —
    /// every webhook test in this module signs against it with
    /// <see cref="SignMandrill"/>, exactly as ReceiveWebhookHandler verifies.
    /// </summary>
    public const string MandrillSigningKey = "test-mandrill-signing-key";

    protected override string DatabaseName => "hsm_api_tests_emails";

    /// <summary>
    /// ReceiveWebhookHandler resolves its signing key from the settings
    /// store, falling back to ISettingSeedSource (configuration key
    /// Settings:Seed:{key}) when no row exists — there is no settings module
    /// seeded yet (Task 9), so this is the deploy-seed path in practice.
    /// </summary>
    protected override void ConfigureModule(IWebHostBuilder builder) =>
        builder.UseSetting(
            "Settings:Seed:COMS_WEBHOOK_SIGNING_KEYS",
            new JsonObject { ["mandrill"] = MandrillSigningKey }.ToJsonString());

    /// <summary>
    /// A minimal EMAIL_EXTERNAL template with an open schema (an empty
    /// object schema matches any data, per TemplateSchema.Validate) — enough
    /// for SendEmailHandler's template lookup and schema validation to
    /// succeed without the Templates module (Task 8) existing yet.
    /// </summary>
    public async Task<string> SeedTemplateAsync()
    {
        var identifier = $"tpl-{Guid.NewGuid():N}";
        await WithDbAsync(async db =>
        {
            db.Templates.Add(new Template
            {
                Id = Guid.NewGuid(),
                Category = TemplateCategories.EmailExternal,
                Name = identifier,
                IsActive = true,
                SchemaJson = "{}",
                Content = "Hello",
            });
            await db.SaveChangesAsync();
            return identifier;
        });
        return identifier;
    }

    /// <summary>Signs <paramref name="rawBody"/> exactly as the real Mandrill
    /// provider would, against <see cref="MandrillSigningKey"/>.</summary>
    public static string SignMandrill(byte[] rawBody)
    {
        // CA5350: HMAC-SHA1 is the Mandrill webhook contract's algorithm —
        // matches MandrillSignatureVerifier's own suppression.
#pragma warning disable CA5350
        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(MandrillSigningKey));
#pragma warning restore CA5350
        return Convert.ToBase64String(hmac.ComputeHash(rawBody));
    }
}
