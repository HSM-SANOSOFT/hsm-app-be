using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Hsm.Web.Auth;

/// <summary>
/// Signed double-submit CSRF (frozen csrf.util.ts): the token is HMAC-bound to
/// the session subject (decoded, unverified, from the access cookie — the
/// HMAC secret provides integrity), stored in an httpOnly cookie, and echoed
/// by the browser in the x-csrf-token header on mutations. Only
/// cookie-authenticated browser mutations are protected: bearer clients
/// cannot be CSRF'd, and pre-session requests carry no token to check.
/// </summary>
public sealed class CsrfProtection(AuthWebOptions options)
{
    public const string HeaderName = "x-csrf-token";
    public const string CookieName = "hsm.x-csrf-token";

    /// <summary>Issues (or reuses) the token for the current session and sets the cookie.</summary>
    public string IssueToken(HttpContext ctx)
    {
        var session = SessionIdentifier(ctx);
        var existing = ctx.Request.Cookies[CookieName];
        var token = existing is not null && IsValid(existing, session)
            ? existing
            : Mint(session);

        ctx.Response.Cookies.Append(CookieName, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = options.CookieSecure,
            Domain = options.CookieDomain,
            SameSite = SameSiteMode.Lax,
            Path = "/",
        });
        return token;
    }

    /// <summary>True when this request must NOT be CSRF-checked.</summary>
    public static bool ShouldSkip(HttpContext ctx)
    {
        var method = ctx.Request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method))
        {
            return true;
        }

        var authorization = ctx.Request.Headers.Authorization.ToString();
        if (authorization.StartsWith("Bearer ", StringComparison.Ordinal))
        {
            return true;
        }

        return !ctx.Request.Cookies.ContainsKey(AuthCookies.AccessCookie);
    }

    /// <summary>Double-submit check: header echoes the cookie and the HMAC binds the session.</summary>
    public bool Validate(HttpContext ctx)
    {
        var cookie = ctx.Request.Cookies[CookieName];
        var header = ctx.Request.Headers[HeaderName].ToString();
        if (string.IsNullOrEmpty(cookie) || string.IsNullOrEmpty(header))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(cookie),
                Encoding.UTF8.GetBytes(header))
            && IsValid(cookie, SessionIdentifier(ctx));
    }

    private string Mint(string session)
    {
        var random = RandomNumberGenerator.GetHexString(64, lowercase: true);
        return $"{random}.{Signature(random, session)}";
    }

    private bool IsValid(string token, string session)
    {
        var separator = token.IndexOf('.', StringComparison.Ordinal);
        if (separator <= 0)
        {
            return false;
        }

        var random = token[..separator];
        var signature = token[(separator + 1)..];
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(signature),
            Encoding.UTF8.GetBytes(Signature(random, session)));
    }

    private string Signature(string random, string session) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(options.CsrfSecret),
            Encoding.UTF8.GetBytes($"{random}|{session}")));

    /// <summary>
    /// The `sub` claim decoded (unverified) from the access cookie, or ""
    /// when absent — the frozen sessionIdentifier.
    /// </summary>
    private static string SessionIdentifier(HttpContext ctx)
    {
        var accessToken = ctx.Request.Cookies[AuthCookies.AccessCookie];
        if (string.IsNullOrEmpty(accessToken))
        {
            return string.Empty;
        }

        var segments = accessToken.Split('.');
        if (segments.Length < 2)
        {
            return string.Empty;
        }

        try
        {
            var payload = Convert.FromBase64String(PadBase64(segments[1]));
            using var json = JsonDocument.Parse(payload);
            return json.RootElement.TryGetProperty("sub", out var sub) && sub.ValueKind == JsonValueKind.String
                ? sub.GetString() ?? string.Empty
                : string.Empty;
        }
        catch (FormatException)
        {
            return string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private static string PadBase64(string base64Url)
    {
        var value = base64Url.Replace('-', '+').Replace('_', '/');
        return (value.Length % 4) switch
        {
            2 => value + "==",
            3 => value + "=",
            _ => value,
        };
    }
}
