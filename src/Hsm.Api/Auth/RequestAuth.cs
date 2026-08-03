using Hsm.Application.Abstractions;
using Hsm.Application.Auth;
using Hsm.Application.Errors;
using Hsm.Contracts.Auth;

namespace Hsm.Api.Auth;

/// <summary>
/// Request AUTHENTICATION — who is calling — and nothing else. The frozen
/// guard chain was AuthJwtAtGuard → RolesGuard → OnboardingGuard; only the
/// first link is a transport concern, and it is all that remains here. Roles
/// and onboarding are properties of the request, declared on the request type
/// and enforced once by
/// <see cref="Hsm.Application.Abstractions.Behaviors.AuthorizationBehavior{TRequest,TResult}"/>,
/// so an endpoint that forgets to gate cannot fail open — it can only fail to
/// supply an actor, which the pipeline answers with 401.
///
/// Token transport is dual: httpOnly cookie FIRST, then the Authorization
/// bearer header — so browser/SSR and integration clients both resolve.
///
/// This is the REST door's copy. <c>Hsm.Web</c> keeps a transport-only twin:
/// the Blazor shell reads the same access cookie to authenticate a session,
/// but it authenticates no requests and installs no actor this way — its
/// actor comes from the circuit, through <c>ShellActor</c>.
/// </summary>
public static class RequestAuth
{
    private const string AccessPrincipalItem = "Hsm.RequestAuth.AccessPrincipal";
    private const string RefreshPrincipalItem = "Hsm.RequestAuth.RefreshPrincipal";
    private const string ActorItem = "Hsm.RequestAuth.Actor";

    /// <summary>The raw access token: access cookie, then bearer header.</summary>
    public static string? AccessToken(HttpContext ctx) =>
        Cookie(ctx, AuthCookiePolicy.AccessTokenName) ?? Bearer(ctx);

    /// <summary>The raw refresh token: refresh cookie, then bearer header.</summary>
    public static string? RefreshToken(HttpContext ctx) =>
        Cookie(ctx, AuthCookiePolicy.RefreshTokenName) ?? Bearer(ctx);

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
    /// Authenticate the access token and publish the caller as the actor the
    /// pipeline authorizes against. This is the whole edge now: it establishes
    /// identity and decides nothing. Returns the principal.
    /// </summary>
    public static async Task<AuthPrincipal> GateAsync(HttpContext ctx)
    {
        var principal = await AuthenticateAsync(ctx, TokenKind.Access);
        await InstallActorAsync(ctx, principal);
        return principal;
    }

    /// <summary>
    /// Publishes the authenticated principal as the request's actor, with
    /// onboarding derived by <see cref="RequestActorFactory"/> — the frozen
    /// OnboardingGuard's exemptions and its authoritative database fallback
    /// included. Routes that authenticate by hand rather than through
    /// <see cref="GateAsync"/> must call this, or they reach the pipeline with
    /// no actor and every non-anonymous request 401s despite a perfectly valid
    /// principal.
    /// </summary>
    public static async Task InstallActorAsync(HttpContext ctx, AuthPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(principal);
        var factory = ctx.RequestServices.GetRequiredService<RequestActorFactory>();
        ctx.Items[ActorItem] = await factory.CreateAsync(
            principal.Id, principal.Roles, principal.OnboardingCompletedAt, ctx.RequestAborted);
    }

    /// <summary>The actor installed on this context, or null when none was.</summary>
    public static RequestActor? InstalledActor(HttpContext? ctx) =>
        ctx is not null && ctx.Items.TryGetValue(ActorItem, out var actor)
            ? actor as RequestActor
            : null;
}
