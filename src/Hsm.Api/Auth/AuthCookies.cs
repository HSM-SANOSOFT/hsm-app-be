using Hsm.Application.Auth;
using Hsm.Contracts.Auth;

namespace Hsm.Api.Auth;

/// <summary>Cookie/CSRF posture bound from configuration (frozen COOKIE_* envs).</summary>
public sealed class AuthWebOptions
{
    public bool CookieSecure { get; set; }
    public string? CookieDomain { get; set; }
    public string CsrfSecret { get; set; } = string.Empty;
}

/// <summary>
/// Dual-transport auth cookies (frozen auth-cookie.util.ts): the body keeps
/// returning the token pair (integrations unchanged); browsers additionally
/// get httpOnly cookies. The names, paths, SameSite modes and lifetimes are
/// <see cref="AuthCookiePolicy"/>'s — shared with <c>Hsm.Web</c>, which signs
/// the same browser in from its own screen. Only the ~30 lines that apply
/// them to this host's <see cref="HttpResponse"/> live here.
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
            AuthCookiePolicy.AccessTokenName, tokens.AccessToken,
            AccessOptions(options, AuthCookiePolicy.AccessMaxAge));
        ctx.Response.Cookies.Append(
            AuthCookiePolicy.RefreshTokenName, tokens.RefreshToken,
            RefreshOptions(options, AuthCookiePolicy.RefreshMaxAge));
    }

    /// <summary>Clears both cookies — options must match how they were set.</summary>
    public static void Clear(HttpContext ctx, AuthWebOptions options)
    {
        ctx.Response.Cookies.Delete(AuthCookiePolicy.AccessTokenName, AccessOptions(options, maxAge: null));
        ctx.Response.Cookies.Delete(AuthCookiePolicy.RefreshTokenName, RefreshOptions(options, maxAge: null));
    }

    private static CookieOptions AccessOptions(AuthWebOptions options, TimeSpan? maxAge) => new()
    {
        HttpOnly = true,
        Secure = options.CookieSecure,
        Domain = options.CookieDomain,
        SameSite = AccessSameSite,
        Path = AuthCookiePolicy.AccessPath,
        MaxAge = maxAge,
    };

    private static CookieOptions RefreshOptions(AuthWebOptions options, TimeSpan? maxAge) => new()
    {
        HttpOnly = true,
        Secure = options.CookieSecure,
        Domain = options.CookieDomain,
        SameSite = RefreshSameSite,
        Path = AuthCookiePolicy.RefreshPath,
        MaxAge = maxAge,
    };
}
