using Hsm.Contracts.Auth;

namespace Hsm.Web.Auth;

/// <summary>
/// The shell's half of the frozen token transport: where the access token is
/// read from, and nothing else. Cookie FIRST, then the Authorization bearer
/// header, exactly as the REST door reads it — that shared reading is what
/// makes one sign-in serve both doors.
///
/// This host authenticates nothing beyond that and installs no actor here.
/// After the Hsm.Api split it serves no /v1 route, so there is no request
/// edge to gate; the only consumer is
/// <see cref="HsmCookieAuthenticationHandler"/>, which turns the token into
/// the session <c>ClaimsPrincipal</c> the Blazor shell authorizes pages with.
/// The pipeline's actor is published from the circuit by <see cref="ShellActor"/>.
/// Validation, the frozen 401 family, and actor installation live in
/// <c>Hsm.Api.Auth.RequestAuth</c>, where the requests that need them are.
/// </summary>
public static class RequestAuth
{
    /// <summary>The raw access token: access cookie, then bearer header.</summary>
    public static string? AccessToken(HttpContext ctx) =>
        Cookie(ctx, AuthCookiePolicy.AccessTokenName) ?? Bearer(ctx);

    public static string? Bearer(HttpContext ctx)
    {
        var authorization = ctx.Request.Headers.Authorization.ToString();
        return authorization.StartsWith("Bearer ", StringComparison.Ordinal)
            ? authorization["Bearer ".Length..].Trim()
            : null;
    }

    private static string? Cookie(HttpContext ctx, string name) =>
        ctx.Request.Cookies.TryGetValue(name, out var value) ? value : null;
}
