using Hsm.Application.Auth;

namespace Hsm.Web.Auth;

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
/// get httpOnly cookies. Access: SameSite=Lax, path=/, 15m. Refresh:
/// SameSite=Strict, path-scoped to /v1/auth, 1d.
/// </summary>
public static class AuthCookies
{
    public const string AccessCookie = "access_token";
    public const string RefreshCookie = "refresh_token";
    public const string RefreshCookiePath = "/v1/auth";

    private static readonly TimeSpan AccessMaxAge = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RefreshMaxAge = TimeSpan.FromDays(1);

    public static void Set(HttpContext ctx, AuthWebOptions options, TokenPair tokens)
    {
        ctx.Response.Cookies.Append(AccessCookie, tokens.AccessToken, AccessOptions(options, AccessMaxAge));
        ctx.Response.Cookies.Append(RefreshCookie, tokens.RefreshToken, RefreshOptions(options, RefreshMaxAge));
    }

    /// <summary>Clears both cookies — options must match how they were set.</summary>
    public static void Clear(HttpContext ctx, AuthWebOptions options)
    {
        ctx.Response.Cookies.Delete(AccessCookie, AccessOptions(options, maxAge: null));
        ctx.Response.Cookies.Delete(RefreshCookie, RefreshOptions(options, maxAge: null));
    }

    private static CookieOptions AccessOptions(AuthWebOptions options, TimeSpan? maxAge) => new()
    {
        HttpOnly = true,
        Secure = options.CookieSecure,
        Domain = options.CookieDomain,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        MaxAge = maxAge,
    };

    private static CookieOptions RefreshOptions(AuthWebOptions options, TimeSpan? maxAge) => new()
    {
        HttpOnly = true,
        Secure = options.CookieSecure,
        Domain = options.CookieDomain,
        SameSite = SameSiteMode.Strict,
        Path = RefreshCookiePath,
        MaxAge = maxAge,
    };
}
