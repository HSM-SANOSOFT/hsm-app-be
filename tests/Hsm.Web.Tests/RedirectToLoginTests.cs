using Bunit.TestDoubles;
using Hsm.Web.Layout;
using Microsoft.Extensions.DependencyInjection;

namespace Hsm.Web.Tests;

/// <summary>
/// Unauthenticated redirect (plan U17, DoD C6): the router's NotAuthorized
/// branch renders <see cref="RedirectToLogin"/>, which must send the visitor
/// to the sign-in page carrying the return URL.
/// </summary>
public sealed class RedirectToLoginTests : MudTestContext
{
    [Fact]
    public void Navigates_to_the_login_page_with_the_return_url()
    {
        var navigation = Services.GetRequiredService<BunitNavigationManager>();
        navigation.NavigateTo("usuarios");

        Render<RedirectToLogin>();

        // History is newest-first in bUnit.
        var target = navigation.History.First();
        Assert.StartsWith("login", target.Uri, StringComparison.Ordinal);
        Assert.Contains("returnUrl=usuarios", target.Uri, StringComparison.Ordinal);
        // forceLoad: the browser re-enters the host so the cookie session is
        // re-evaluated by the server, not just the router.
        Assert.True(target.Options.ForceLoad);
    }

    [Fact]
    public void Navigates_to_login_without_return_url_from_the_root()
    {
        var navigation = Services.GetRequiredService<BunitNavigationManager>();

        Render<RedirectToLogin>();

        Assert.Equal("login", navigation.History.Single().Uri);
    }
}
