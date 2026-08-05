using Hsm.Api.OpenApi;
using Hsm.Application.Abstractions;
using Hsm.Application.Identity;
using Hsm.Application.Identity.Commands.CompleteOnboarding;
using Hsm.Application.Identity.Commands.ForgotPassword;
using Hsm.Application.Identity.Commands.Login;
using Hsm.Application.Identity.Commands.LogoutIntegration;
using Hsm.Application.Identity.Commands.RecoverUsername;
using Hsm.Application.Identity.Commands.RefreshIntegrationTokens;
using Hsm.Application.Identity.Commands.Register;
using Hsm.Application.Identity.Commands.RegisterIntegration;
using Hsm.Application.Identity.Commands.ResetPassword;
using Hsm.Application.Identity.Queries.GetMe;
using Hsm.Domain.Identity;
using Hsm.Infrastructure.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;

namespace Hsm.Api.Identity;

/// <summary>
/// The identity resource: the account lifecycle a person goes through, and the
/// three routes an integration account needs because it cannot hold a cookie.
///
/// <para>Every delegate does transport work only: bind, dispatch, project,
/// choose a status — plus, on the four routes that open or close a session,
/// write the cookie. That split is the module's organising rule. Verifying a
/// credential, creating an account and completing onboarding are application
/// work a Blazor circuit needs identically, so they are commands; issuing the
/// cookie that represents the result is transport, and only this door does
/// it.</para>
///
/// <para>There is no authentication call and no role check here. The actor is
/// installed by <see cref="HsmActorMiddleware"/> for every route and the
/// policy rides on the dispatched request type, so an endpoint cannot fail
/// open by forgetting either.</para>
/// </summary>
public static class IdentityEndpoints
{
    /// <summary>
    /// Non-committal acknowledgement for the two recovery routes. ONE constant,
    /// returned whether or not the account exists — the moment these diverge,
    /// the routes answer "is this address registered?".
    /// </summary>
    private const string GenericRecoveryMessage = "If an account exists, we have sent an email.";

    /// <summary>
    /// The per-IP edge throttle on the three recovery routes: 10 per 60 seconds,
    /// partitioned by <c>{remoteIp}:{path}</c> (configured in
    /// <c>Program.cs</c>). It is the SECOND of the module's two limits — the
    /// first, five requests per rolling hour per ACCOUNT, lives inside
    /// <c>ForgotPasswordHandler</c> and produces a 429 of its own. Neither
    /// subsumes the other: this one bounds a single caller probing many
    /// addresses, that one bounds many callers hammering a single account.
    /// </summary>
    public const string RecoveryRateLimitPolicy = "auth-recovery";

    /// <summary>
    /// The per-IP throttle on refresh: 30 per 60 seconds (configured in
    /// <c>Program.cs</c>). Its own policy rather than a share of
    /// <see cref="RecoveryRateLimitPolicy"/>, because it answers a different
    /// threat with a different budget.
    ///
    /// <para>The recovery limit bounds a prober asking "is this address
    /// registered?". This one bounds an anonymous caller replaying digests to
    /// trip reuse detection — which REVOKES an integration's credential, so
    /// every attempt that lands is a denial of service against a machine
    /// account, and the attempts are otherwise free and repeatable.</para>
    ///
    /// <para>30, because an integration holding a day-long access token refreshes
    /// on the order of once a day; even a badly written client is bounded by its
    /// own traffic, so this is orders of magnitude of headroom for legitimate
    /// use while capping an attacker at 30 tries a minute per address. It does
    /// not make the attack impossible — nothing at this layer can, against a
    /// distributed caller holding a leaked token — it makes it cost something.</para>
    /// </summary>
    public const string RefreshRateLimitPolicy = "identity-refresh";

    public static void MapIdentityEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var identity = app.MapGroup("/api/v1/identity").WithTags("Identity");

        identity.MapPost("/register", Register)
            .WithSummary("Register a patient account and start a session.")
            .Produces<MeResource>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        // Login CAN answer 401: an unrecognised username, wrong password, or a
        // locked-out account all surface as the same UnauthorizedException
        // (LoginHandler's own doc comment) — anonymous reachability and "may
        // still refuse with 401" are not in tension here.
        identity.MapPost("/login", Login)
            .WithSummary("Sign in and start a session.")
            .Produces<MeResource>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesValidationProblem()
            .ExchangesCredentials();

        identity.MapPost("/logout", Logout)
            .WithSummary("End the calling session.")
            .Produces(StatusCodes.Status204NoContent);

        identity.MapGet("/me", Me)
            .WithSummary("Read the calling user's own account.")
            .Produces<MeResource>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        identity.MapPost("/onboarding", Onboarding)
            .WithSummary("Complete first-login onboarding.")
            .Produces<MeResource>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesValidationProblem();

        identity.MapGet("/csrf", Csrf)
            .WithSummary("Issue an antiforgery token.")
            .Produces<AntiforgeryTokenResource>();

        // 202, not 200: the work these two acknowledge — finding the account,
        // sending the mail — is deliberately not reported on, so "accepted" is
        // the only honest status. The rate limit here is the per-IP throttle.
        identity.MapPost("/password/forgot", ForgotPassword)
            .WithSummary("Begin a password reset.")
            .Produces<AcknowledgedResource>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .RequireRateLimiting(RecoveryRateLimitPolicy);

        identity.MapPost("/password/reset", ResetPassword)
            .WithSummary("Consume a reset token and set a new password.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .RequireRateLimiting(RecoveryRateLimitPolicy);

        identity.MapPost("/username/recover", RecoverUsername)
            .WithSummary("Email the username for an account.")
            .Produces<AcknowledgedResource>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .RequireRateLimiting(RecoveryRateLimitPolicy);

        // ----- integrations ------------------------------------------------
        //
        // The one caller that cannot hold a session cookie. Registering and
        // signing out are administration, gated by their commands'
        // [RequireRole(admin)]; refreshing is the machine's own, and the token
        // it presents IS the credential.
        identity.MapPost("/integrations/register", RegisterIntegration)
            .WithSummary("Provision an integration account and issue its first credential.")
            .Produces<IntegrationTokenResource>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesValidationProblem();

        identity.MapPost("/integrations/logout", LogoutIntegration)
            .WithSummary("Revoke an integration account's credential by presenting it.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesValidationProblem();

        // Under /identity rather than /identity/integrations because it is the
        // machine speaking for itself, not an admin acting on it — the same
        // split login and register sit on either side of. Anonymous, but the
        // presented token can be unknown, spent, or reused — the SAME 401
        // refusal RefreshIntegrationTokensHandler answers every one of those
        // with (its own doc comment).
        identity.MapPost("/refresh", Refresh)
            .WithSummary("Redeem an integration's refresh token for a fresh credential.")
            .Produces<IntegrationTokenResource>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesValidationProblem()
            .RequireRateLimiting(RefreshRateLimitPolicy)
            .ExchangesCredentials();
    }

    private static async Task<IResult> Register(
        RegisterRequest request,
        IDispatcher dispatcher,
        HsmSessionSignIn sessions,
        UserManager<HsmUser> users,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sessions);

        var user = await dispatcher.Send(
            new RegisterCommand(
                request.Username,
                request.Email,
                request.Password,
                request.FirstName,
                request.FirstLastName,
                request.SecondName,
                request.SecondLastName,
                request.PhoneNumber,
                request.Gender),
            ct);

        await sessions.SignInAsync(user, ct);

        // Location is /me rather than /api/v1/users/{id}: that collection is
        // admin-only, and a patient who has just registered would get a 403
        // following it.
        return Results.Created("/api/v1/identity/me", await MeAsync(user, users));
    }

    private static async Task<IResult> Login(
        LoginRequest request,
        IDispatcher dispatcher,
        HsmSessionSignIn sessions,
        UserManager<HsmUser> users,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sessions);

        // The COMMAND verifies the credential (and owns lockout accounting);
        // the ENDPOINT writes the cookie. Credential checking is application
        // logic that a Blazor circuit needs too; writing a cookie is transport
        // and only this door does it.
        //
        // Each sign-in opens its OWN revocable session — see HsmSessionSignIn,
        // which is also where the reason for SignInAsync over
        // PasswordSignInAsync lives.
        var user = await dispatcher.Send(
            new LoginCommand(request.Username, request.Password), ct);
        await sessions.SignInAsync(user, ct);
        return Results.Ok(await MeAsync(user, users));
    }

    /// <summary>
    /// Sign-out. Nothing is dispatched because nothing in the application
    /// changes: the session IS the cookie, and this hands it back.
    ///
    /// Nothing is dispatched: closing a session is not application state a
    /// Blazor circuit would need to change identically, it is this door taking
    /// its own credential back.
    ///
    /// <para>It revokes SERVER-SIDE, which is the whole point.
    /// <c>SignOutAsync</c> alone only asks the browser to drop the cookie, and
    /// an Identity cookie is self-contained — a copy taken beforehand from a
    /// shared workstation, a proxy log or malware would keep authenticating for
    /// the rest of the sliding window. <see cref="HsmSessionSignIn.SignOutAsync"/>
    /// deletes this session's row first, so the next request presenting that
    /// copy finds no session and is refused. Scoped to THIS session: the same
    /// person stays signed in on their other devices, which is what
    /// distinguishes signing out from a password change.</para>
    ///
    /// <para>204 even for a caller with no session, and reachable without an
    /// authentication check for the same reason: refusing would leave a
    /// browser holding a stale or unreadable cookie with no way to be rid of
    /// it, which is the one state this route exists to fix. (An authenticated
    /// caller still passes antiforgery on the way in, like every other
    /// cookie-borne mutation.)</para>
    /// </summary>
    private static async Task<IResult> Logout(HsmSessionSignIn sessions, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        await sessions.SignOutAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> Me(
        IDispatcher dispatcher, UserManager<HsmUser> users, CancellationToken ct) =>
        Results.Ok(await MeAsync(await dispatcher.Send(new GetMeQuery(), ct), users));

    private static async Task<IResult> Onboarding(
        OnboardingRequest request,
        HttpContext context,
        IDispatcher dispatcher,
        HsmSessionSignIn sessions,
        UserManager<HsmUser> users,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sessions);

        var user = await dispatcher.Send(
            new CompleteOnboardingCommand(
                request.NewPassword, request.PhoneNumber, request.ConfirmEmail),
            ct);

        // Setting the password rotated the security stamp, so every cookie for
        // this account — the caller's included — is now stale and would be
        // refused on its next request. Every OTHER session dying is the point;
        // this one is not, and on the shell it presents as "I finished
        // onboarding and got bounced to the sign-in screen". RefreshAsync
        // reissues THIS session's cookie with the new stamp, KEEPING its
        // session id, and carries the now-completed onboarding claim with it.
        //
        // Only when the caller actually has a cookie session, for the same
        // reason ChangeOwnPassword guards it: a bearer caller must not be handed
        // one as a side effect.
        if ((await context.AuthenticateAsync(IdentityConstants.ApplicationScheme)).Succeeded)
        {
            await sessions.RefreshAsync(user, ct);
        }

        return Results.Ok(await MeAsync(user, users));
    }

    private static IResult Csrf(HttpContext context, IAntiforgery antiforgery)
    {
        ArgumentNullException.ThrowIfNull(antiforgery);

        // GetAndStoreTokens writes the cookie half and hands back the half the
        // caller must echo in X-XSRF-TOKEN.
        var tokens = antiforgery.GetAndStoreTokens(context);
        return Results.Ok(new AntiforgeryTokenResource(tokens.RequestToken!));
    }

    private static async Task<IResult> ForgotPassword(
        ForgotPasswordRequest request, IDispatcher dispatcher, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        await dispatcher.Send(new ForgotPasswordCommand(request.Email), ct);

        // Identical body and status for a known and an unknown address. The
        // handler is what makes that true — it returns the same Unit either way
        // and swallows a failing send — and this line is what keeps it true.
        return Results.Accepted(value: new AcknowledgedResource(GenericRecoveryMessage));
    }

    private static async Task<IResult> ResetPassword(
        ResetPasswordRequest request, IDispatcher dispatcher, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        // An unknown, expired or already-spent token all leave the handler as
        // the SAME ValidationException on `token`, so this route's failure is
        // as uninformative as its success.
        await dispatcher.Send(new ResetPasswordCommand(request.Token, request.NewPassword), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> RecoverUsername(
        RecoverUsernameRequest request, IDispatcher dispatcher, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        await dispatcher.Send(new RecoverUsernameCommand(request.Email), ct);
        return Results.Accepted(value: new AcknowledgedResource(GenericRecoveryMessage));
    }

    private static async Task<IResult> RegisterIntegration(
        RegisterIntegrationRequest request, IDispatcher dispatcher, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var tokens = await dispatcher.Send(
            new RegisterIntegrationCommand(request.Name, request.Description, request.Functionality),
            ct);

        // 201 with no Location: there is no route that reads an integration
        // account back, and pointing at one that does not exist would be worse
        // than pointing at nothing. The credential in the body is the whole
        // result, and it is the only time it can be read.
        return Results.Json(TokensFor(tokens), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> LogoutIntegration(
        LogoutIntegrationRequest request, IDispatcher dispatcher, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        await dispatcher.Send(new LogoutIntegrationCommand(request.Token), ct);
        return Results.NoContent();
    }

    /// <summary>
    /// Rotate an integration's credential.
    ///
    /// <para>The route does no verification of its own — it binds a string and
    /// dispatches. That is not an omission: an opaque refresh token has no
    /// signature to check and no claim to read, so the ONLY check that exists is
    /// the handler's lookup against the stored digest, and splitting a second,
    /// weaker one out to the edge would invite a reader to believe the edge was
    /// doing something.</para>
    ///
    /// <para>Anonymous, because the refresh token is the credential: this route
    /// is reachable precisely when the access token is not. A signed-in browser
    /// gains nothing by calling it — it holds a sliding session cookie, not a
    /// refresh token, and a cookie matches no row here.</para>
    /// </summary>
    private static async Task<IResult> Refresh(
        RefreshRequest request, IDispatcher dispatcher, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var tokens = await dispatcher.Send(
            new RefreshIntegrationTokensCommand(request.RefreshToken), ct);
        return Results.Ok(TokensFor(tokens));
    }

    private static IntegrationTokenResource TokensFor(IntegrationTokens tokens) =>
        new(tokens.AccessToken,
            tokens.RefreshToken,
            (int)IntegrationTokenIssuer.AccessTokenLifetime.TotalSeconds);

    /// <summary>
    /// The one projection every user-facing route in this file returns. Roles come from
    /// Identity's own assignment table, which is a second read — the same one
    /// the users module pays — and it is why <see cref="MeResource.From"/>
    /// takes them rather than reading them off the entity.
    /// </summary>
    private static async Task<MeResource> MeAsync(HsmUser user, UserManager<HsmUser> users) =>
        MeResource.From(user, [.. await users.GetRolesAsync(user)]);
}
