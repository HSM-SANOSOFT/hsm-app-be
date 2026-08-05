using System.Security.Claims;
using System.Text.Json.Nodes;
using Hsm.Api.Http;
using Hsm.Application.Abstractions;
using Hsm.Application.Auth;
using Hsm.Application.Auth.Commands.CompleteOnboarding;
using Hsm.Application.Auth.Commands.ForgotPassword;
using Hsm.Application.Auth.Commands.GeneratePin;
using Hsm.Application.Auth.Commands.LogoutIntegration;
using Hsm.Application.Auth.Commands.RecoverUsername;
using Hsm.Application.Auth.Commands.RefreshTokens;
using Hsm.Application.Auth.Commands.ResetPassword;
using Hsm.Application.Auth.Commands.Signup;
using Hsm.Application.Auth.Commands.SignupIntegration;
using Hsm.Application.Auth.Commands.ValidatePin;
using Hsm.Application.Errors;

namespace Hsm.Api.Auth;

/// <summary>
/// What is left of the frozen /v1/auth surface while Task 13 rewrites it into
/// <c>/api/v1/identity</c>. Success bodies still ride the frozen envelope; POST
/// returns 201 and GET 200, matching the frozen runtime.
///
/// <para><b>Three routes left in Task 12, with the mechanism they were.</b>
/// <c>POST /login</c> and <c>GET /csrf</c> were replaced in the same commit by
/// <c>POST /api/v1/identity/login</c> and <c>GET /api/v1/identity/csrf</c>:
/// keeping the old pair would have meant two sign-in routes writing two
/// different session mechanisms and two antiforgery issuers, which is the
/// divergence this rewrite exists to remove. <c>GET /logout</c> went with the
/// JWT cookies it cleared: its frozen contract is "present a token, have its
/// stored hash revoked", and after Task 12 a browser has no token to present.
/// Task 13 restores sign-out as <c>POST /api/v1/identity/logout</c> →
/// <c>SignInManager.SignOutAsync</c>. <c>GET /refresh</c> lost its BROWSER
/// half only — see its own comment.</para>
///
/// <para><b>Everything else is untouched in contract.</b> The routes below no
/// longer call <c>RequestAuth.GateAsync</c> because
/// <c>HsmActorMiddleware</c> installs the actor for every route before they
/// run; the roles and onboarding requirements they enforce are, as before, the
/// dispatched request type's own and are enforced once in the pipeline. Body
/// shape validation for these routes is still not in the pipeline — Task 13
/// adds it with the reshape.</para>
/// </summary>
public static class AuthEndpoints
{
    /// <summary>Frozen non-committal acknowledgement for the recovery endpoints.</summary>
    private const string GenericRecoveryMessage = "If an account exists, we have sent an email.";

    public const string RecoveryRateLimitPolicy = "auth-recovery";

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var auth = app.MapGroup("/v1/auth");

        auth.MapPost("/signup", Signup);
        auth.MapGet("/refresh", Refresh);
        auth.MapPost("/onboarding", Onboarding);
        auth.MapPost("/signup/integration", SignupIntegration);
        auth.MapPost("/logout/integration", LogoutIntegration);
        auth.MapGet("/profile", (Delegate)Profile);
        auth.MapPost("/pin/generate", (Delegate)PinGenerate);
        auth.MapPost("/pin/validate", (Delegate)PinValidate);

        // The frozen recovery routes carried a tighter per-IP throttle
        // (@Throttle long: 10 per 60s) on top of the per-account limit.
        auth.MapPost("/password/forgot", ForgotPassword).RequireRateLimiting(RecoveryRateLimitPolicy);
        auth.MapPost("/password/reset", ResetPassword).RequireRateLimiting(RecoveryRateLimitPolicy);
        auth.MapPost("/username/recover", RecoverUsername).RequireRateLimiting(RecoveryRateLimitPolicy);
    }

    private static async Task<IResult> Signup(HttpContext ctx, IDispatcher dispatcher)
    {
        // Client-supplied "roles" are accepted in the frozen JSON but IGNORED —
        // public signup always provisions a Patient (frozen
        // PublicSignupPayloadDto) — and System.Text.Json drops unmapped
        // members by default, so no explicit handling is needed here.
        var command = await ctx.Request.ReadValidatedJsonAsync<SignupCommand>(ctx.RequestAborted)
            ?? new SignupCommand(
                string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, null, null, null, null);

        // The token pair is still the response body, unchanged, and it is still
        // a usable credential — the access token verifies against the same
        // secret the bearer handler uses. What is gone is the cookie side
        // channel: a browser signs in at /api/v1/identity/login now. Task 13
        // turns this route into POST /api/v1/identity/register, which returns
        // the created user and a session cookie.
        var tokens = await dispatcher.Send(command, ctx.RequestAborted);
        return ApiEnvelope.Success(ctx, StatusCodes.Status201Created, TokensJson(tokens));
    }

    /// <summary>
    /// Integration refresh rotation. The BROWSER half of this route is gone
    /// with the JWT cookies it read: a browser holds a sliding, encrypted
    /// session and has no refresh token to present. The INTEGRATION half was
    /// never a browser concern and is still the only way an integration
    /// exchanges its long-lived refresh token for a fresh pair —
    /// <c>TokenIssuer.RotateRefreshTokenAsync</c>'s integration branch, and the
    /// credential the operator is handed by the integration-accounts screen.
    /// Task 14 replaces it with <c>POST /api/v1/identity/refresh</c> over an
    /// opaque token; until then, deleting it would strand every issued refresh
    /// token.
    /// </summary>
    private static async Task<IResult> Refresh(HttpContext ctx, IDispatcher dispatcher)
    {
        // Read from the Authorization header ONLY. The token is presented
        // explicitly here, never ambiently — which is also why the adaptive
        // scheme's bearer handler cannot have authenticated this request: a
        // refresh token is signed with the refresh secret, so HttpContext.User
        // is anonymous and RefreshTokensCommand's [AllowAnonymousRequest] is
        // what lets it through. The TOKEN is the credential, and the two checks
        // that verify it — signature/expiry here, stored hash in the handler —
        // both still run.
        var rawToken = Bearer(ctx);
        if (string.IsNullOrEmpty(rawToken))
        {
            throw new UnauthorizedException();
        }

        var codec = ctx.RequestServices.GetRequiredService<IAuthTokenCodec>();
        var validation = await codec.ValidateAsync(rawToken, TokenKind.Refresh);
        var principal = validation.Principal
            ?? throw new UnauthorizedException(validation.IsExpired ? "token expired" : "Invalid token");

        var tokens = await dispatcher.Send(
            new RefreshTokensCommand(principal, rawToken), ctx.RequestAborted);
        return ApiEnvelope.Success(ctx, StatusCodes.Status200OK, TokensJson(tokens));
    }

    /// <summary>The raw <c>Authorization: Bearer</c> value, or null.</summary>
    private static string? Bearer(HttpContext ctx)
    {
        var authorization = ctx.Request.Headers.Authorization.ToString();
        return authorization.StartsWith("Bearer ", StringComparison.Ordinal)
            ? authorization["Bearer ".Length..].Trim()
            : null;
    }

    private static async Task<IResult> Onboarding(HttpContext ctx, IDispatcher dispatcher)
    {
        // @AllowPending — a pending user must be able to reach this.
        // CompleteOnboardingCommand is [AllowPendingOnboarding], so a pending
        // actor passes the pipeline on this route and only this route.
        //
        // No re-issued session is needed after completing onboarding: the
        // cookie's onboarding claim is a CACHE, and RequestActorFactory falls
        // back to the now-updated user row whenever it is absent — which is
        // exactly the state a still-pending session's cookie is in.
        var command = await ctx.Request.ReadValidatedJsonAsync<CompleteOnboardingCommand>(ctx.RequestAborted)
            ?? new CompleteOnboardingCommand(string.Empty, string.Empty, string.Empty);

        var tokens = await dispatcher.Send(command, ctx.RequestAborted);
        return ApiEnvelope.Success(ctx, StatusCodes.Status201Created, TokensJson(tokens));
    }

    private static async Task<IResult> SignupIntegration(HttpContext ctx, IDispatcher dispatcher)
    {
        // The admin requirement lives on SignupIntegrationCommand's
        // [RequireRole(Roles.Admin)] and is enforced once, in the pipeline,
        // against the actor the middleware already installed.
        var command = await ctx.Request.ReadValidatedJsonAsync<SignupIntegrationCommand>(ctx.RequestAborted)
            ?? new SignupIntegrationCommand(string.Empty, string.Empty, string.Empty);

        // Tokens in the body only — integrations never use cookies.
        var tokens = await dispatcher.Send(command, ctx.RequestAborted);
        return ApiEnvelope.Success(ctx, StatusCodes.Status201Created, TokensJson(tokens));
    }

    private static async Task<IResult> LogoutIntegration(HttpContext ctx, IDispatcher dispatcher)
    {
        // The frozen edge's admin requirement rides on
        // LogoutIntegrationCommand's [RequireRole(Roles.Admin)].
        var command = await ctx.Request.ReadValidatedJsonAsync<LogoutIntegrationCommand>(ctx.RequestAborted)
            ?? new LogoutIntegrationCommand(string.Empty);

        await dispatcher.Send(command, ctx.RequestAborted);
        return ApiEnvelope.Success(ctx, StatusCodes.Status201Created, includeData: false);
    }

    private static IResult Profile(HttpContext ctx)
    {
        // @AllowPending; authenticated by either half of the adaptive scheme.
        // Nothing is dispatched, so this is the one route that reads
        // HttpContext.User directly rather than the actor derived from it.
        if (ctx.User.Identity?.IsAuthenticated != true)
        {
            throw new UnauthorizedException();
        }

        return ApiEnvelope.Success(ctx, StatusCodes.Status200OK, ProfileJson(ctx.User));
    }

    private static async Task<IResult> PinGenerate(HttpContext ctx, IDispatcher dispatcher)
    {
        // NOT @AllowPending in the frozen controller — pending users are
        // blocked, and GeneratePinCommand's (absent) policy is what says so now.
        var command = await ctx.Request.ReadValidatedJsonAsync<GeneratePinCommand>(ctx.RequestAborted)
            ?? new GeneratePinCommand(string.Empty, string.Empty);

        await dispatcher.Send(command, ctx.RequestAborted);
        return ApiEnvelope.Success(ctx, StatusCodes.Status201Created, includeData: false);
    }

    private static async Task<IResult> PinValidate(HttpContext ctx, IDispatcher dispatcher)
    {
        var command = await ctx.Request.ReadValidatedJsonAsync<ValidatePinCommand>(ctx.RequestAborted)
            ?? new ValidatePinCommand(string.Empty, string.Empty, 0);

        await dispatcher.Send(command, ctx.RequestAborted);
        return ApiEnvelope.Success(ctx, StatusCodes.Status201Created, includeData: false);
    }

    private static async Task<IResult> ForgotPassword(HttpContext ctx, IDispatcher dispatcher)
    {
        var command = await ctx.Request.ReadValidatedJsonAsync<ForgotPasswordCommand>(ctx.RequestAborted)
            ?? new ForgotPasswordCommand(string.Empty);

        await dispatcher.Send(command, ctx.RequestAborted);
        return ApiEnvelope.Success(
            ctx,
            StatusCodes.Status201Created,
            new JsonObject { ["message"] = GenericRecoveryMessage });
    }

    private static async Task<IResult> ResetPassword(HttpContext ctx, IDispatcher dispatcher)
    {
        var command = await ctx.Request.ReadValidatedJsonAsync<ResetPasswordCommand>(ctx.RequestAborted)
            ?? new ResetPasswordCommand(string.Empty, string.Empty);

        await dispatcher.Send(command, ctx.RequestAborted);
        return ApiEnvelope.Success(
            ctx,
            StatusCodes.Status201Created,
            new JsonObject { ["message"] = "Password updated." });
    }

    private static async Task<IResult> RecoverUsername(HttpContext ctx, IDispatcher dispatcher)
    {
        var command = await ctx.Request.ReadValidatedJsonAsync<RecoverUsernameCommand>(ctx.RequestAborted)
            ?? new RecoverUsernameCommand(string.Empty);

        await dispatcher.Send(command, ctx.RequestAborted);
        return ApiEnvelope.Success(
            ctx,
            StatusCodes.Status201Created,
            new JsonObject { ["message"] = GenericRecoveryMessage });
    }

    private static JsonObject TokensJson(TokenPair tokens) => new()
    {
        ["access_token"] = tokens.AccessToken,
        ["refresh_token"] = tokens.RefreshToken,
    };

    /// <summary>
    /// The frozen profile payload, read off the authenticated principal instead
    /// of off a hand-decoded JWT. An integration's bearer token carries the
    /// frozen claim names verbatim (the bearer handler maps nothing), so that
    /// caller — the one this route exists for — sees exactly what it saw
    /// before. A cookie session has no <c>iat</c>/<c>exp</c> to report and
    /// carries Identity's claim names; those members are simply absent for it.
    /// Task 13 replaces the whole shape with <c>GET /api/v1/identity/me</c>,
    /// which reads the user row and does not depend on either claim layout.
    /// </summary>
    private static JsonObject ProfileJson(ClaimsPrincipal user)
    {
        var profile = new JsonObject
        {
            ["id"] = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub"),
        };

        Copy(profile, "username", user.FindFirstValue("username") ?? user.FindFirstValue(ClaimTypes.Name));
        Copy(profile, "email", user.FindFirstValue("email") ?? user.FindFirstValue(ClaimTypes.Email));
        Copy(profile, "firstName", user.FindFirstValue("firstName"));
        Copy(profile, "firstLastName", user.FindFirstValue("firstLastName"));
        Copy(profile, "name", user.FindFirstValue("name"));

        var roles = new JsonArray();
        foreach (var role in user.Identities.SelectMany(i => i.FindAll(i.RoleClaimType)))
        {
            roles.Add(role.Value);
        }

        profile["roles"] = roles;

        // Users carry the claim (possibly null while pending); integrations
        // never did, and absence is the frozen encoding of that.
        var onboarding = user.FindFirstValue("onboardingCompletedAt")
            ?? user.FindFirstValue(HsmClaims.OnboardingCompletedAt);
        if (onboarding is not null)
        {
            profile["onboardingCompletedAt"] = onboarding;
        }

        // Unix seconds, and NUMBERS in the frozen payload — a claim value is
        // always a string, so it is parsed back rather than copied across.
        CopyNumber(profile, "iat", user.FindFirstValue("iat"));
        CopyNumber(profile, "exp", user.FindFirstValue("exp"));
        return profile;
    }

    private static void Copy(JsonObject profile, string member, string? value)
    {
        if (value is not null)
        {
            profile[member] = value;
        }
    }

    private static void CopyNumber(JsonObject profile, string member, string? value)
    {
        if (long.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var number))
        {
            profile[member] = number;
        }
    }
}
