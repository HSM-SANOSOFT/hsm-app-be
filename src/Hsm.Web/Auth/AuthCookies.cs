using Hsm.Application.Auth;
using Hsm.Contracts.Auth;

namespace Hsm.Web.Auth;

/// <summary>Cookie posture bound from configuration (frozen COOKIE_* envs).</summary>
public sealed class AuthWebOptions
{
    public bool CookieSecure { get; set; }
    public string? CookieDomain { get; set; }
}

/// <summary>
/// The shell's half of the frozen auth-cookie plumbing. This host issues
/// cookies from exactly one place — the sign-in screen's
/// <see cref="Hsm.Web.Services.SignInUiService"/>, which dispatches the same
/// <c>LoginCommand</c> the REST door does — so a browser signed in here is
/// signed in at <c>Hsm.Api</c> too, and the other way round. That only holds
/// because both hosts read the names, paths, SameSite modes and lifetimes
/// from <see cref="AuthCookiePolicy"/>; all that is per-host is the handful
/// of lines below that write them onto this host's response.
/// </summary>
public static class AuthCookies
{
    private static readonly SameSiteMode AccessSameSite =
        Enum.Parse<SameSiteMode>(AuthCookiePolicy.AccessSameSite);

    private static readonly SameSiteMode RefreshSameSite =
        Enum.Parse<SameSiteMode>(AuthCookiePolicy.RefreshSameSite);

    public static void Set(HttpContext ctx, AuthWebOptions options, TokenPair tokens)
    {
        ctx.Response.Cookies.Append(
            AuthCookiePolicy.AccessTokenName, tokens.AccessToken, AccessOptions(options));
        ctx.Response.Cookies.Append(
            AuthCookiePolicy.RefreshTokenName, tokens.RefreshToken, RefreshOptions(options));
    }

    private static CookieOptions AccessOptions(AuthWebOptions options) => new()
    {
        HttpOnly = true,
        Secure = options.CookieSecure,
        Domain = options.CookieDomain,
        SameSite = AccessSameSite,
        Path = AuthCookiePolicy.AccessPath,
        MaxAge = AuthCookiePolicy.AccessMaxAge,
    };

    private static CookieOptions RefreshOptions(AuthWebOptions options) => new()
    {
        HttpOnly = true,
        Secure = options.CookieSecure,
        Domain = options.CookieDomain,
        SameSite = RefreshSameSite,
        Path = AuthCookiePolicy.RefreshPath,
        MaxAge = AuthCookiePolicy.RefreshMaxAge,
    };
}
