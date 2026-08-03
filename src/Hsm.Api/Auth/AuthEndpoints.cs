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
using Hsm.Domain.Identity;

namespace Hsm.Api.Auth;

/// <summary>
/// The fourteen frozen /v1/auth operations. Success bodies ride the frozen
/// envelope; POST returns 201 and GET 200, matching the frozen runtime
/// (NestJS defaults — the snapshot's documented 200s were doc-generator
/// drift, and integration consumers saw 201).
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
        var body = await BodyValidator.ReadAsync(ctx);
        var username = body.RequiredString("username");
        var email = body.RequiredString("email");
        var password = body.RequiredString("password", minLength: 8);
        var firstName = body.RequiredString("firstName");
        var secondName = body.OptionalString("secondName");
        var firstLastName = body.RequiredString("firstLastName");
        var secondLastName = body.OptionalString("secondLastName");
        var phoneNumber = body.OptionalString("phoneNumber");
        var gender = body.OptionalString("gender");
        // Client-supplied roles are accepted but IGNORED — public signup
        // always provisions a Patient (frozen PublicSignupPayloadDto).
        body.IgnoredArray("roles");
        body.RejectUnknownFields();
        body.ThrowIfInvalid();

        var tokens = await dispatcher.Send(
            new SignupCommand(
                username, email, password, firstName, firstLastName,
                secondName, secondLastName, phoneNumber, gender),
            ctx.RequestAborted);
        AuthCookies.Set(ctx, Options(ctx), tokens);
        return ApiEnvelope.Success(ctx, StatusCodes.Status201Created, TokensJson(tokens));
    }

    private static async Task<IResult> Login(HttpContext ctx, IDispatcher dispatcher)
    {
        // The frozen local guard ran BEFORE validation: missing/non-string
        // credentials surface 401, not 400.
        var body = await BodyValidator.ReadAsync(ctx);
        var username = body.OptionalString("username");
        var password = body.OptionalString("password");
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            throw Hsm.Application.Errors.ApiException.Unauthorized();
        }

        // Whitelist validation now runs BEFORE credential verification rather
        // than between it and token issuance: LoginCommand verifies and issues
        // in one dispatch, and issuing rotates the stored refresh hash, so
        // validating afterwards would sign a caller out of their other sessions
        // on a request that then 400s. See the Task 10 report, J2.
        body.RejectUnknownFields();
        body.ThrowIfInvalid();

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

        var body = await BodyValidator.ReadAsync(ctx);
        var newPassword = body.RequiredString("newPassword", minLength: 8);
        var phoneNumber = body.RequiredString("phoneNumber");
        var confirmEmail = body.RequiredString("confirmEmail", email: true);
        body.RejectUnknownFields();
        body.ThrowIfInvalid();

        var tokens = await dispatcher.Send(
            new CompleteOnboardingCommand(newPassword, phoneNumber, confirmEmail), ctx.RequestAborted);
        AuthCookies.Set(ctx, Options(ctx), tokens);
        return ApiEnvelope.Success(ctx, StatusCodes.Status201Created, TokensJson(tokens));
    }

    private static async Task<IResult> SignupIntegration(HttpContext ctx, IDispatcher dispatcher)
    {
        // Authenticate and install the actor; the admin requirement lives on
        // SignupIntegrationCommand's [RequireRole(Roles.Admin)] and is
        // enforced once, in the pipeline.
        await RequestAuth.GateAsync(ctx);

        var body = await BodyValidator.ReadAsync(ctx);
        var name = body.RequiredString("name");
        var description = body.RequiredString("description");
        var functionality = body.RequiredString("functionality", oneOf: IntegrationFunctionality.All);
        body.RejectUnknownFields();
        body.ThrowIfInvalid();

        // Tokens in the body only — integrations never use cookies.
        var tokens = await dispatcher.Send(
            new SignupIntegrationCommand(name, description, functionality), ctx.RequestAborted);
        return ApiEnvelope.Success(ctx, StatusCodes.Status201Created, TokensJson(tokens));
    }

    private static async Task<IResult> LogoutIntegration(HttpContext ctx, IDispatcher dispatcher)
    {
        // Same chain as SignupIntegration. The frozen edge's admin requirement
        // now rides on LogoutIntegrationCommand's [RequireRole(Roles.Admin)].
        await RequestAuth.GateAsync(ctx);

        var body = await BodyValidator.ReadAsync(ctx);
        var token = body.RequiredString("token");
        body.RejectUnknownFields();
        body.ThrowIfInvalid();

        await dispatcher.Send(new LogoutIntegrationCommand(token), ctx.RequestAborted);
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

        var body = await BodyValidator.ReadAsync(ctx);
        var purpose = body.RequiredString("purpose", oneOf: PinPurposes);
        var target = body.RequiredString("target");
        body.RejectUnknownFields();
        body.ThrowIfInvalid();

        await dispatcher.Send(new GeneratePinCommand(purpose, target), ctx.RequestAborted);
        return ApiEnvelope.Success(ctx, StatusCodes.Status201Created, includeData: false);
    }

    private static async Task<IResult> PinValidate(HttpContext ctx, IDispatcher dispatcher)
    {
        await RequestAuth.GateAsync(ctx);

        var body = await BodyValidator.ReadAsync(ctx);
        var purpose = body.RequiredString("purpose", oneOf: PinPurposes);
        var target = body.RequiredString("target");
        var code = body.RequiredNumber("code");
        body.RejectUnknownFields();
        body.ThrowIfInvalid();

        await dispatcher.Send(new ValidatePinCommand(purpose, target, code), ctx.RequestAborted);
        return ApiEnvelope.Success(ctx, StatusCodes.Status201Created, includeData: false);
    }

    private static async Task<IResult> ForgotPassword(HttpContext ctx, IDispatcher dispatcher)
    {
        var body = await BodyValidator.ReadAsync(ctx);
        var email = body.RequiredString("email", email: true);
        body.RejectUnknownFields();
        body.ThrowIfInvalid();

        await dispatcher.Send(new ForgotPasswordCommand(email), ctx.RequestAborted);
        return ApiEnvelope.Success(
            ctx,
            StatusCodes.Status201Created,
            new JsonObject { ["message"] = GenericRecoveryMessage });
    }

    private static async Task<IResult> ResetPassword(HttpContext ctx, IDispatcher dispatcher)
    {
        var body = await BodyValidator.ReadAsync(ctx);
        var token = body.RequiredString("token");
        var newPassword = body.RequiredString("newPassword", minLength: 8);
        body.RejectUnknownFields();
        body.ThrowIfInvalid();

        await dispatcher.Send(new ResetPasswordCommand(token, newPassword), ctx.RequestAborted);
        return ApiEnvelope.Success(
            ctx,
            StatusCodes.Status201Created,
            new JsonObject { ["message"] = "Password updated." });
    }

    private static async Task<IResult> RecoverUsername(HttpContext ctx, IDispatcher dispatcher)
    {
        var body = await BodyValidator.ReadAsync(ctx);
        var email = body.RequiredString("email", email: true);
        body.RejectUnknownFields();
        body.ThrowIfInvalid();

        await dispatcher.Send(new RecoverUsernameCommand(email), ctx.RequestAborted);
        return ApiEnvelope.Success(
            ctx,
            StatusCodes.Status201Created,
            new JsonObject { ["message"] = GenericRecoveryMessage });
    }

    private static readonly string[] PinPurposes =
    [
        "email_verification",
        "password_reset",
        "identity_verification",
        "integration_approval",
    ];

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
