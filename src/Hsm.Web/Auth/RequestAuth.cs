using Hsm.Application.Abstractions;
using Hsm.Application.Auth;
using Hsm.Application.Errors;
using Hsm.Domain.Identity;

namespace Hsm.Web.Auth;

/// <summary>
/// Request authentication and authorization, reproducing the frozen guard
/// chain (AuthJwtAtGuard → RolesGuard → OnboardingGuard) and its observable
/// errors. Token transport is dual: httpOnly cookie FIRST, then the
/// Authorization bearer header — so browser/SSR and integration clients both
/// resolve.
/// </summary>
public static class RequestAuth
{
    private const string AccessPrincipalItem = "Hsm.RequestAuth.AccessPrincipal";
    private const string RefreshPrincipalItem = "Hsm.RequestAuth.RefreshPrincipal";

    /// <summary>The raw access token: access cookie, then bearer header.</summary>
    public static string? AccessToken(HttpContext ctx) =>
        Cookie(ctx, AuthCookies.AccessCookie) ?? Bearer(ctx);

    /// <summary>The raw refresh token: refresh cookie, then bearer header.</summary>
    public static string? RefreshToken(HttpContext ctx) =>
        Cookie(ctx, AuthCookies.RefreshCookie) ?? Bearer(ctx);

    public static string? Bearer(HttpContext ctx)
    {
        var authorization = ctx.Request.Headers.Authorization.ToString();
        return authorization.StartsWith("Bearer ", StringComparison.Ordinal)
            ? authorization["Bearer ".Length..].Trim()
            : null;
    }

    private static string? Cookie(HttpContext ctx, string name) =>
        ctx.Request.Cookies.TryGetValue(name, out var value) ? value : null;

    /// <summary>
    /// Validates the transported token of <paramref name="kind"/> and returns
    /// the principal, or throws the frozen 401 family: missing → "Unauthorized",
    /// expired → "token expired"/TOKEN_EXPIRED, otherwise "Invalid token"/INVALID_TOKEN.
    /// </summary>
    public static async Task<AuthPrincipal> AuthenticateAsync(HttpContext ctx, TokenKind kind)
    {
        var raw = kind == TokenKind.Access ? AccessToken(ctx) : RefreshToken(ctx);
        if (string.IsNullOrEmpty(raw))
        {
            throw ApiException.Unauthorized();
        }

        // Decode-once per request: a second consumer of the same token in the
        // same request reuses the validated principal.
        var cacheKey = kind == TokenKind.Access ? AccessPrincipalItem : RefreshPrincipalItem;
        if (ctx.Items.TryGetValue(cacheKey, out var cached)
            && cached is (string cachedRaw, AuthPrincipal cachedPrincipal)
            && cachedRaw == raw)
        {
            return cachedPrincipal;
        }

        var codec = ctx.RequestServices.GetRequiredService<IAuthTokenCodec>();
        var result = await codec.ValidateAsync(raw, kind);
        if (result.Principal is null)
        {
            throw result.IsExpired
                ? ApiException.Unauthorized("token expired", errorLabel: "TOKEN_EXPIRED")
                : ApiException.Unauthorized("Invalid token", errorLabel: "INVALID_TOKEN");
        }

        ctx.Items[cacheKey] = (raw, result.Principal);
        return result.Principal;
    }

    /// <summary>
    /// The frozen guard chain in one call: authenticate the access token,
    /// enforce roles, then the onboarding gate. Returns the principal.
    /// </summary>
    public static async Task<AuthPrincipal> GateAsync(HttpContext ctx, params string[] requiredRoles)
    {
        var principal = await AuthenticateAsync(ctx, TokenKind.Access);
        RequireRoles(ctx, principal, requiredRoles);
        await RequireOnboardingCompletedAsync(ctx, principal);
        InstallActor(ctx, principal);
        return principal;
    }

    /// <summary>
    /// Publishes the gated principal as the request-scoped actor the
    /// application pipeline authorizes against. Onboarding is recorded as
    /// satisfied because <see cref="RequireOnboardingCompletedAsync"/> has just
    /// enforced the frozen OnboardingGuard — including its admin/integration
    /// exemptions and its authoritative database fallback, neither of which the
    /// token claims alone can express.
    /// </summary>
    private static void InstallActor(HttpContext ctx, AuthPrincipal principal)
    {
        ctx.RequestServices.GetRequiredService<AmbientPrincipal>().Set(
            new RequestActor(principal.Id, principal.Roles, OnboardingCompleted: true));
    }

    /// <summary>
    /// The frozen RolesGuard: developer is env-gated; admin passes everything;
    /// otherwise at least one required role must be held.
    /// </summary>
    public static void RequireRoles(HttpContext ctx, AuthPrincipal principal, params string[] requiredRoles)
    {
        if (principal.Roles.Contains(Roles.Developer))
        {
            var environment = ctx.RequestServices.GetRequiredService<IEnvironmentPolicy>();
            if (!environment.IsDev)
            {
                throw ApiException.Forbidden("Developer role is not permitted in this environment");
            }

            return;
        }

        if (requiredRoles.Length == 0 || principal.Roles.Contains(Roles.Admin))
        {
            return;
        }

        if (!requiredRoles.Any(principal.Roles.Contains))
        {
            throw ApiException.Forbidden("Insufficient permissions");
        }
    }

    /// <summary>
    /// The frozen OnboardingGuard for routes that do NOT allow pending users:
    /// integrations and admins are exempt; a completed claim is trusted; a
    /// pending claim defers to the authoritative database row and fails closed.
    /// </summary>
    public static async Task RequireOnboardingCompletedAsync(HttpContext ctx, AuthPrincipal principal)
    {
        if (principal.IsIntegration || principal.Roles.Contains(Roles.Admin))
        {
            return;
        }

        if (principal.OnboardingCompletedAt is not null)
        {
            return;
        }

        var users = ctx.RequestServices.GetRequiredService<IUserStore>();
        var (found, onboardingCompletedAt) = await users.OnboardingStateAsync(Guid.Parse(principal.Id));
        if (!found)
        {
            throw ApiException.Forbidden("Account is no longer available");
        }

        if (onboardingCompletedAt is null)
        {
            throw ApiException.Forbidden("Onboarding required: complete first-login onboarding to continue");
        }
    }
}
