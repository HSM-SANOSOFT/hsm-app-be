using Bunit;
using Hsm.Contracts.Ui;
using Hsm.Web.Pages.Admin;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Hsm.Web.Tests;

/// <summary>
/// The integration accounts screen (plan U18, screen 3). The critical
/// property: the issued secret renders in the issuing view EXACTLY ONCE —
/// after dismissal no state remains that could bring it back, and further
/// renders never resurface it.
/// </summary>
public sealed class IntegrationAccountsPageTests : MudTestContext
{
    private const string Secret = "FAKE_ACCESS_TOKEN";

    [Fact]
    public void Renders_the_account_listing()
    {
        var fake = new FakeIntegrationAccountsUiService
        {
            Accounts =
            [
                new IntegrationAccountDto(
                    Guid.NewGuid().ToString(), "erp-bridge", "Puente al ERP", "prod",
                    IsActive: true, HasActiveToken: true),
            ],
        };
        Services.AddSingleton<IIntegrationAccountsUiService>(fake);

        var cut = Render<IntegrationAccounts>();

        Assert.Contains("Cuentas de integración", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("erp-bridge", cut.Markup, StringComparison.Ordinal);
        // No secret anywhere before an issuing action.
        Assert.DoesNotContain(Secret, cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Provisioning_shows_the_secret_exactly_once_and_dismissal_destroys_it()
    {
        var fake = new FakeIntegrationAccountsUiService();
        Services.AddSingleton<IIntegrationAccountsUiService>(fake);

        var cut = Render<IntegrationAccounts>();
        cut.Find("input[data-testid='new-account-name']").Change("erp-bridge");
        cut.Find("input[data-testid='new-account-description']").Change("Puente al ERP");
        cut.Find("button[data-testid='provision-submit']").Click();

        // The issuing view shows both tokens.
        Assert.Contains(Secret, cut.Markup, StringComparison.Ordinal);
        Assert.Contains("FAKE_REFRESH_TOKEN", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("una única vez", cut.Markup, StringComparison.Ordinal);

        cut.Find("button[data-testid='dismiss-tokens']").Click();

        // Gone — and a fresh render cycle cannot re-produce it: the component
        // holds no other copy and the service was only asked once.
        Assert.DoesNotContain(Secret, cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("FAKE_REFRESH_TOKEN", cut.Markup, StringComparison.Ordinal);
        cut.Render(ParameterView.Empty);
        Assert.DoesNotContain(Secret, cut.Markup, StringComparison.Ordinal);
        Assert.Equal(1, fake.ProvisionCalls);
        Assert.Equal(0, fake.IssueCalls);
    }

    [Fact]
    public void Issuing_for_an_existing_account_shows_the_secret_once_then_gone()
    {
        var account = new IntegrationAccountDto(
            Guid.NewGuid().ToString(), "lab-sync", "Sincroniza laboratorio", "staging",
            IsActive: true, HasActiveToken: false);
        var fake = new FakeIntegrationAccountsUiService { Accounts = [account] };
        Services.AddSingleton<IIntegrationAccountsUiService>(fake);

        var cut = Render<IntegrationAccounts>();
        cut.Find("button[data-testid='issue-tokens-lab-sync']").Click();

        Assert.Contains(Secret, cut.Markup, StringComparison.Ordinal);

        cut.Find("button[data-testid='dismiss-tokens']").Click();

        Assert.DoesNotContain(Secret, cut.Markup, StringComparison.Ordinal);
        Assert.Equal(1, fake.IssueCalls);
    }

    [Fact]
    public void Revoking_invokes_the_service_for_the_account()
    {
        var account = new IntegrationAccountDto(
            Guid.NewGuid().ToString(), "erp-bridge", "Puente al ERP", "prod",
            IsActive: true, HasActiveToken: true);
        var fake = new FakeIntegrationAccountsUiService { Accounts = [account] };
        Services.AddSingleton<IIntegrationAccountsUiService>(fake);

        var cut = Render<IntegrationAccounts>();
        cut.Find("button[data-testid='revoke-tokens-erp-bridge']").Click();

        Assert.Equal([account.Id], fake.RevokeCalls);
    }
}
