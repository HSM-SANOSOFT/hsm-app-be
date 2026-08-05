using Hsm.Infrastructure.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hsm.Tests.Auth;

/// <summary>
/// The session cookie's posture, asserted where it is DECIDED rather than
/// where it happens to be observed.
///
/// <para>Every host in the test suite runs on one origin, so nothing there can
/// see a cookie that is scoped to the wrong domain — and "one sign-in serves
/// both doors" fails silently the moment <c>Hsm.Api</c> and <c>Hsm.Web</c> sit
/// on sibling subdomains without <c>Auth:CookieDomain</c> reaching the
/// options. That is exactly the shape of bug this suite exists to catch: real,
/// deployment-only, invisible to an in-memory host.</para>
/// </summary>
public class CookieConfigurationTests
{
    [Fact]
    public void The_configured_cookie_domain_reaches_the_session_and_antiforgery_cookies()
    {
        var provider = Build(("Auth:CookieDomain", ".hospital.test"));

        Assert.Equal(".hospital.test", SessionCookie(provider).Domain);
        Assert.Equal(".hospital.test", AntiforgeryCookie(provider).Domain);
    }

    [Fact]
    public void No_configured_domain_leaves_the_cookies_host_only()
    {
        var provider = Build();

        // Null, not "" — an empty Domain is a different cookie than no Domain.
        Assert.Null(SessionCookie(provider).Domain);
        Assert.Null(AntiforgeryCookie(provider).Domain);
    }

    [Fact]
    public void The_session_cookie_is_named_http_only_strict_and_rooted_at_the_site()
    {
        var cookie = SessionCookie(Build());

        Assert.Equal("hsm.session", cookie.Name);
        Assert.True(cookie.HttpOnly);
        Assert.Equal(SameSiteMode.Strict, cookie.SameSite);
        Assert.Equal("/", cookie.Path);
    }

    [Fact]
    public void Cookie_secure_is_configuration_driven_and_defaults_to_same_as_request()
    {
        // Always in a TLS deployment; SameAsRequest by default so a developer's
        // plain-HTTP container still gets a session at all.
        Assert.Equal(CookieSecurePolicy.SameAsRequest, SessionCookie(Build()).SecurePolicy);
        Assert.Equal(
            CookieSecurePolicy.Always,
            SessionCookie(Build(("Auth:CookieSecure", "true"))).SecurePolicy);
        Assert.Equal(
            CookieSecurePolicy.Always,
            AntiforgeryCookie(Build(("Auth:CookieSecure", "true"))).SecurePolicy);
    }

    [Fact]
    public void The_security_stamp_is_revalidated_on_every_request()
    {
        // Zero, not the framework's 30 minutes: the stamp is this system's only
        // session-revocation channel (ChangeUserRoleHandler bumps it), and a
        // revocation honoured half an hour later is not one.
        var options = Build().GetRequiredService<IOptions<SecurityStampValidatorOptions>>().Value;

        Assert.Equal(TimeSpan.Zero, options.ValidationInterval);
    }

    [Fact]
    public void A_missing_jwt_access_secret_fails_at_composition_rather_than_at_first_call()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new ConfigurationBuilder().Build();

        var thrown = Assert.Throws<InvalidOperationException>(
            () => services.AddHsmIdentityAuthentication(configuration));

        Assert.Contains("Auth:JwtAccessSecret", thrown.Message, StringComparison.Ordinal);
    }

    private static CookieBuilder SessionCookie(ServiceProvider provider) =>
        provider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme)
            .Cookie;

    private static CookieBuilder AntiforgeryCookie(ServiceProvider provider) =>
        provider.GetRequiredService<IOptions<AntiforgeryOptions>>().Value.Cookie;

    private static ServiceProvider Build(params (string Key, string Value)[] settings)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                settings
                    .Append(("Auth:JwtAccessSecret", "unit_test_at_secret_0123456789abcdef"))
                    .Select(setting => new KeyValuePair<string, string?>(setting.Item1, setting.Item2)))
            .Build();

        services.AddHsmIdentityAuthentication(configuration);
        return services.BuildServiceProvider();
    }
}
