using Bunit;
using Hsm.Contracts.Ui;
using Hsm.Web.Components.Pages.Admin;
using Microsoft.Extensions.DependencyInjection;

namespace Hsm.Web.Components.Tests;

/// <summary>
/// The settings screen (plan U18, screen 4): items render per category with
/// secrets masked (never plaintext), the audit trail renders, and saving
/// sends only the effective changes through the UI service.
/// </summary>
public sealed class SettingsPageTests : MudTestContext
{
    private static FakeSettingsAdminUiService EmailFake() => new()
    {
        ItemsByCategory =
        {
            [UiSettingCategories.Email] =
            [
                new SettingItemDto(
                    "SMTP_ADDRESS", UiSettingCategories.Email,
                    IsSecret: false, IsSet: true, Value: "smtp.hsm.test"),
                new SettingItemDto(
                    "SMTP_PASSWORD", UiSettingCategories.Email,
                    IsSecret: true, IsSet: true, Value: "********"),
            ],
        },
        AuditByCategory =
        {
            [UiSettingCategories.Email] =
            [
                new SettingAuditEntryDto(
                    "SMTP_ADDRESS", "admin-user-id", "smtp.old.test", "smtp.hsm.test",
                    DateTimeOffset.UtcNow),
            ],
        },
    };

    [Fact]
    public void Renders_settings_with_secrets_masked()
    {
        Services.AddSingleton<ISettingsAdminUiService>(EmailFake());

        var cut = Render<AppSettings>();

        Assert.Contains("Configuración", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("SMTP_ADDRESS", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("smtp.hsm.test", cut.Markup, StringComparison.Ordinal);

        // The secret renders as a password input carrying only the mask —
        // the DTO itself never holds plaintext (masked at the handler).
        var secretInput = cut.Find("input[data-testid='setting-SMTP_PASSWORD']");
        Assert.Equal("password", secretInput.GetAttribute("type"));
        Assert.Equal("********", secretInput.GetAttribute("placeholder"));
        Assert.NotEqual("********", secretInput.GetAttribute("value"));
    }

    [Fact]
    public void Renders_the_audit_trail_with_actor_and_values()
    {
        Services.AddSingleton<ISettingsAdminUiService>(EmailFake());

        var cut = Render<AppSettings>();

        Assert.Contains("Historial de cambios", cut.Markup, StringComparison.Ordinal);
        var audit = cut.Find("[data-testid='settings-audit']");
        Assert.Contains("admin-user-id", audit.TextContent, StringComparison.Ordinal);
        Assert.Contains("smtp.old.test", audit.TextContent, StringComparison.Ordinal);
        Assert.Contains("smtp.hsm.test", audit.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Saving_sends_only_the_changed_settings()
    {
        var fake = EmailFake();
        Services.AddSingleton<ISettingsAdminUiService>(fake);

        var cut = Render<AppSettings>();
        cut.Find("input[data-testid='setting-SMTP_ADDRESS']").Change("smtp.nuevo.test");
        cut.Find("button[data-testid='save-settings']").Click();

        var (category, changes) = Assert.Single(fake.UpdateCalls);
        Assert.Equal(UiSettingCategories.Email, category);
        var change = Assert.Single(changes);
        Assert.Equal("SMTP_ADDRESS", change.Key);
        Assert.Equal("smtp.nuevo.test", change.Value);
    }

    [Fact]
    public void Blank_secret_input_is_not_sent_as_a_change()
    {
        var fake = EmailFake();
        Services.AddSingleton<ISettingsAdminUiService>(fake);

        var cut = Render<AppSettings>();
        // Nothing typed into the secret; only save pressed.
        cut.Find("button[data-testid='save-settings']").Click();

        Assert.Empty(fake.UpdateCalls);
    }
}
