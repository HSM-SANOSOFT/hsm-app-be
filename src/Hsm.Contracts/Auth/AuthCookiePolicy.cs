namespace Hsm.Contracts.Auth;

/// <summary>
/// The session-cookie posture both doors must agree on (frozen
/// auth-cookie.util.ts). <c>Hsm.Api</c> issues these cookies from
/// <c>POST /v1/auth/login</c>; <c>Hsm.Web</c> issues the same ones from its
/// sign-in screen and reads the access cookie to authenticate the Blazor
/// shell. A browser signed in through either door must be signed in through
/// the other, so the names, paths, SameSite modes and lifetimes are stated
/// once, here, and each host keeps only the ~30 lines that apply them to its
/// own <c>HttpResponse</c>. The JWT crypto behind the values is already shared
/// through <c>IAuthTokenCodec</c>.
///
/// <para><b>Why SameSite is a string.</b> Hsm.Contracts is the
/// client-isolation boundary's only shared surface and must stay a leaf
/// assembly — no <c>Hsm.*</c> references (ContractsPurityTests) and, just as
/// importantly, no ASP.NET Core framework reference, which is what naming
/// <c>Microsoft.AspNetCore.Http.SameSiteMode</c> here would drag in. The
/// values are the exact <c>SameSiteMode</c> member names, so each host maps
/// them with a single <c>Enum.Parse</c> that fails loudly on a typo rather
/// than silently downgrading a cookie.</para>
/// </summary>
public static class AuthCookiePolicy
{
    /// <summary>Access-token cookie name (frozen: <c>access_token</c>).</summary>
    public const string AccessTokenName = "access_token";

    /// <summary>Refresh-token cookie name (frozen: <c>refresh_token</c>).</summary>
    public const string RefreshTokenName = "refresh_token";

    /// <summary>Access cookie path — the whole site, because every surface reads it.</summary>
    public const string AccessPath = "/";

    /// <summary>
    /// Refresh cookie path — scoped to the auth routes so the long-lived
    /// credential is not attached to every request.
    /// </summary>
    public const string RefreshPath = "/v1/auth";

    /// <summary>Access cookie <c>SameSite</c> mode, as the enum member name.</summary>
    public const string AccessSameSite = "Lax";

    /// <summary>Refresh cookie <c>SameSite</c> mode, as the enum member name.</summary>
    public const string RefreshSameSite = "Strict";

    /// <summary>Access cookie lifetime (frozen: 15 minutes).</summary>
    public static readonly TimeSpan AccessMaxAge = TimeSpan.FromMinutes(15);

    /// <summary>Refresh cookie lifetime (frozen: 1 day).</summary>
    public static readonly TimeSpan RefreshMaxAge = TimeSpan.FromDays(1);
}
