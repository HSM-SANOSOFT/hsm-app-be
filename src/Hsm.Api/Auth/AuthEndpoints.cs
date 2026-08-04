using System.Text.Json.Nodes;
using Hsm.Api.Http;
using Hsm.Application.Abstractions;
using Hsm.Application.Auth;
using Hsm.Application.Auth.Commands.CompleteOnboarding;
using Hsm.Application.Auth.Commands.ForgotPassword;
using Hsm.Application.Auth.Commands.GeneratePin;
using Hsm.Application.Auth.Commands.Login;
using Hsm.Application.Auth.Commands.Logout;
using Hsm.Application.Auth.Commands.LogoutIntegration;
using Hsm.Application.Auth.Commands.RecoverUsername;
using Hsm.Application.Auth.Commands.RefreshTokens;
using Hsm.Application.Auth.Commands.ResetPassword;
using Hsm.Application.Auth.Commands.Signup;
using Hsm.Application.Auth.Commands.SignupIntegration;
using Hsm.Application.Auth.Commands.ValidatePin;
using Hsm.Contracts.Auth;

namespace Hsm.Api.Auth;

/// <summary>
/// The fourteen frozen /v1/auth operations. Success bodies ride the frozen
/// envelope; POST returns 201 and GET 200, matching the frozen runtime
/// (NestJS defaults — the snapshot's documented 200s were doc-generator
/// drift, and integration consumers saw 201). Body shape validation for these
/// routes is not yet in the pipeline — Identity tasks (11-14) reshape this
/// module and add its validators; until then a malformed payload reaches the
/// handler as-is.
/// </summary>
public static class AuthEndpoints
{
    /// <summary>Frozen non-committal acknowledgement for the recovery endpoints.</summary>
    private const string GenericRecoveryMessage = "If an account exists, we have sent an email.";

    public const string RecoveryRateLimitPolicy = "auth-recovery";

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/v1/auth");

        auth.MapPost("/signup", Signup);
        auth.MapPost("/login", Login);
        auth.MapGet("/logout", Logout);
        auth.MapGet("/refresh", Refresh);
        auth.MapPost("/onboarding", Onboarding);
        auth.MapPost("/signup/integration", SignupIntegration);
        auth.MapPost("/logout/integration", LogoutIntegration);
        auth.MapGet("/profile", (Delegate)Profile);
        auth.MapGet("/csrf", Csrf);
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
        var command = await ctx.Request.ReadFromJsonAsync<SignupCommand>(ctx.RequestAborted)
            ?? new SignupCommand(
                string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, null, null, null, null);

        var tokens = await dispatcher.Send(command, ctx.RequestAborted);
        AuthCookies.Set(ctx, Options(ctx), tokens);
        return ApiEnvelope.Success(ctx, StatusCodes.Status201Created, TokensJson(tokens));
    }

    private static async Task<IResult> Login(HttpContext ctx, IDispatcher dispatcher)
    {
        // The frozen local guard ran BEFORE validation: missing/non-string
        // credentials surface 401, not 400.
        var body = await ctx.Request.ReadFromJsonAsync<LoginBody>(ctx.RequestAborted);
        var username = body?.Username;
        var password = body?.Password;
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            throw new Hsm.Application.Errors.UnauthorizedException();
        }

        var tokens = await dispatcher.Send(new LoginCommand(username, password), ctx.RequestAborted);
        AuthCookies.Set(ctx, Options(ctx), tokens);
        return ApiEnvelope.Success(ctx, StatusCodes.Status201Created, TokensJson(tokens));
    }

    private static async Task<IResult> Logout(HttpContext ctx, IDispatcher dispatcher)
    {
        // Bearer header (integrations) first, then access cookie, then the
        // refresh cookie. Cookies are cleared regardless of the outcome.
        var token = RequestAuth.Bearer(ctx)
            ?? ctx.Request.Cookies[AuthCookiePolicy.AccessTokenName]
            ?? ctx.Request.Cookies[AuthCookiePolicy.RefreshTokenName];
        AuthCookies.Clear(ctx, Options(ctx));
        // No actor is installed: the token IS the credential here and it may be
        // expired or unverifiable, which the handler answers with the frozen
        // 401 messages. LogoutCommand is [AllowAnonymousRequest] for exactly
        // that reason — see the Task 10 report, J1.
        await dispatcher.Send(new LogoutCommand(token), ctx.RequestAborted);
        return ApiEnvelope.Success(ctx, StatusCodes.Status200OK, includeData: false);
    }

    private static async Task<IResult> Refresh(HttpContext ctx, IDispatcher dispatcher)
    {
        var principal = await RequestAuth.AuthenticateAsync(ctx, TokenKind.Refresh);
        var rawToken = RequestAuth.RefreshToken(ctx)!;
        // No actor: RefreshTokensCommand is [AllowAnonymousRequest] because the
        // refresh token is its credential, and it carries the principal this
        // edge just validated.
        var tokens = await dispatcher.Send(
            new RefreshTokensCommand(principal, rawToken), ctx.RequestAborted);
        AuthCookies.Set(ctx, Options(ctx), tokens);
        return ApiEnvelope.Success(ctx, StatusCodes.Status200OK, TokensJson(tokens));
    }

    private static async Task<IResult> Onboarding(HttpContext ctx, IDispatcher dispatcher)
    {
        // @AllowPending — a pending user must be able to reach this, and the
        // actor carries the principal's REAL onboarding state.
        // CompleteOnboardingCommand is [AllowPendingOnboarding], so a pending
        // actor passes the pipeline on this route and only this route.
        var principal = await RequestAuth.AuthenticateAsync(ctx, TokenKind.Access);
        await RequestAuth.InstallActorAsync(ctx, principal);

        var command = await ctx.Request.ReadFromJsonAsync<CompleteOnboardingCommand>(ctx.RequestAborted)
            ?? new CompleteOnboardingCommand(string.Empty, string.Empty, string.Empty);

        var tokens = await dispatcher.Send(command, ctx.RequestAborted);
        AuthCookies.Set(ctx, Options(ctx), tokens);
        return ApiEnvelope.Success(ctx, StatusCodes.Status201Created, TokensJson(tokens));
    }

    private static async Task<IResult> SignupIntegration(HttpContext ctx, IDispatcher dispatcher)
    {
        // Authenticate and install the actor; the admin requirement lives on
        // SignupIntegrationCommand's [RequireRole(Roles.Admin)] and is
        // enforced once, in the pipeline.
        await RequestAuth.GateAsync(ctx);

        var command = await ctx.Request.ReadFromJsonAsync<SignupIntegrationCommand>(ctx.RequestAborted)
            ?? new SignupIntegrationCommand(string.Empty, string.Empty, string.Empty);

        // Tokens in the body only — integrations never use cookies.
        var tokens = await dispatcher.Send(command, ctx.RequestAborted);
        return ApiEnvelope.Success(ctx, StatusCodes.Status201Created, TokensJson(tokens));
    }

    private static async Task<IResult> LogoutIntegration(HttpContext ctx, IDispatcher dispatcher)
    {
        // Same chain as SignupIntegration. The frozen edge's admin requirement
        // now rides on LogoutIntegrationCommand's [RequireRole(Roles.Admin)].
        await RequestAuth.GateAsync(ctx);

        var command = await ctx.Request.ReadFromJsonAsync<LogoutIntegrationCommand>(ctx.RequestAborted)
            ?? new LogoutIntegrationCommand(string.Empty);

        await dispatcher.Send(command, ctx.RequestAborted);
        return ApiEnvelope.Success(ctx, StatusCodes.Status201Created, includeData: false);
    }

    private static async Task<IResult> Profile(HttpContext ctx)
    {
        // @AllowPending — the token payload is returned as-is (id, claims, iat,
        // exp). Nothing is dispatched, so no actor is installed.
        var principal = await RequestAuth.AuthenticateAsync(ctx, TokenKind.Access);
        return ApiEnvelope.Success(ctx, StatusCodes.Status200OK, ProfileJson(principal));
    }

    private static async Task<IResult> Csrf(HttpContext ctx, CsrfProtection csrf)
    {
        // @AllowPending; authenticated (cookie session or bearer). Dispatches
        // nothing.
        await RequestAuth.AuthenticateAsync(ctx, TokenKind.Access);
        var token = csrf.IssueToken(ctx);
        return ApiEnvelope.Success(ctx, StatusCodes.Status200OK, new JsonObject { ["csrfToken"] = token });
    }

    private static async Task<IResult> PinGenerate(HttpContext ctx, IDispatcher dispatcher)
    {
        // NOT @AllowPending in the frozen controller — pending users are
        // blocked, and GeneratePinCommand's (absent) policy is what says so now.
        await RequestAuth.GateAsync(ctx);

        var command = await ctx.Request.ReadFromJsonAsync<GeneratePinCommand>(ctx.RequestAborted)
            ?? new GeneratePinCommand(string.Empty, string.Empty);

        await dispatcher.Send(command, ctx.RequestAborted);
        return ApiEnvelope.Success(ctx, StatusCodes.Status201Created, includeData: false);
    }

    private static async Task<IResult> PinValidate(HttpContext ctx, IDispatcher dispatcher)
    {
        await RequestAuth.GateAsync(ctx);

        var command = await ctx.Request.ReadFromJsonAsync<ValidatePinCommand>(ctx.RequestAborted)
            ?? new ValidatePinCommand(string.Empty, string.Empty, 0);

        await dispatcher.Send(command, ctx.RequestAborted);
        return ApiEnvelope.Success(ctx, StatusCodes.Status201Created, includeData: false);
    }

    private static async Task<IResult> ForgotPassword(HttpContext ctx, IDispatcher dispatcher)
    {
        var command = await ctx.Request.ReadFromJsonAsync<ForgotPasswordCommand>(ctx.RequestAborted)
            ?? new ForgotPasswordCommand(string.Empty);

        await dispatcher.Send(command, ctx.RequestAborted);
        return ApiEnvelope.Success(
            ctx,
            StatusCodes.Status201Created,
            new JsonObject { ["message"] = GenericRecoveryMessage });
    }

    private static async Task<IResult> ResetPassword(HttpContext ctx, IDispatcher dispatcher)
    {
        var command = await ctx.Request.ReadFromJsonAsync<ResetPasswordCommand>(ctx.RequestAborted)
            ?? new ResetPasswordCommand(string.Empty, string.Empty);

        await dispatcher.Send(command, ctx.RequestAborted);
        return ApiEnvelope.Success(
            ctx,
            StatusCodes.Status201Created,
            new JsonObject { ["message"] = "Password updated." });
    }

    private static async Task<IResult> RecoverUsername(HttpContext ctx, IDispatcher dispatcher)
    {
        var command = await ctx.Request.ReadFromJsonAsync<RecoverUsernameCommand>(ctx.RequestAborted)
            ?? new RecoverUsernameCommand(string.Empty);

        await dispatcher.Send(command, ctx.RequestAborted);
        return ApiEnvelope.Success(
            ctx,
            StatusCodes.Status201Created,
            new JsonObject { ["message"] = GenericRecoveryMessage });
    }

    /// <summary>The frozen LoginDto surface, read ahead of the local guard below.</summary>
    private sealed record LoginBody(string? Username, string? Password);

    private static AuthWebOptions Options(HttpContext ctx) =>
        ctx.RequestServices.GetRequiredService<AuthWebOptions>();

    private static JsonObject TokensJson(TokenPair tokens) => new()
    {
        ["access_token"] = tokens.AccessToken,
        ["refresh_token"] = tokens.RefreshToken,
    };

    private static JsonObject ProfileJson(AuthPrincipal principal)
    {
        var profile = new JsonObject { ["id"] = principal.Id };
        if (principal.Username is not null)
        {
            profile["username"] = principal.Username;
        }

        if (principal.Email is not null)
        {
            profile["email"] = principal.Email;
        }

        if (principal.FirstName is not null)
        {
            profile["firstName"] = principal.FirstName;
        }

        if (principal.FirstLastName is not null)
        {
            profile["firstLastName"] = principal.FirstLastName;
        }

        if (principal.Name is not null)
        {
            profile["name"] = principal.Name;
        }

        var roles = new JsonArray();
        foreach (var role in principal.Roles)
        {
            roles.Add(role);
        }

        profile["roles"] = roles;
        if (principal.HasOnboardingClaim)
        {
            profile["onboardingCompletedAt"] = principal.OnboardingCompletedAt is null
                ? null
                : JsonValue.Create(principal.OnboardingCompletedAt);
        }

        profile["iat"] = principal.IssuedAt;
        profile["exp"] = principal.ExpiresAt;
        return profile;
    }
}
