using Hsm.Application.Abstractions;
using Hsm.Application.Auth.Commands.CompleteOnboarding;
using Hsm.Application.Auth.Commands.ForgotPassword;
using Hsm.Application.Auth.Commands.Login;
using Hsm.Application.Auth.Commands.RecoverUsername;
using Hsm.Application.Auth.Commands.Register;
using Hsm.Application.Auth.Commands.ResetPassword;
using Hsm.Application.Auth.Queries.GetMe;
using Hsm.Domain.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;

namespace Hsm.Api.Identity;

/// <summary>
/// The identity resource — everything the frozen <c>/v1/auth</c> controller
/// did except the integration routes, which Task 14 brings over as
/// <c>/api/v1/identity/integrations/*</c> and <c>/api/v1/identity/refresh</c>.
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

    public static void MapIdentityEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var identity = app.MapGroup("/api/v1/identity").WithTags("Identity");

        identity.MapPost("/register", Register)
            .WithSummary("Register a patient account and start a session.")
            .Produces<MeResource>(StatusCodes.Status201Created);

        identity.MapPost("/login", Login)
            .WithSummary("Sign in and start a session.")
            .Produces<MeResource>();

        identity.MapPost("/logout", Logout)
            .WithSummary("End the calling session.")
            .Produces(StatusCodes.Status204NoContent);

        identity.MapGet("/me", Me)
            .WithSummary("Read the calling user's own account.")
            .Produces<MeResource>();

        identity.MapPost("/onboarding", Onboarding)
            .WithSummary("Complete first-login onboarding.")
            .Produces<MeResource>();

        identity.MapGet("/csrf", Csrf)
            .WithSummary("Issue an antiforgery token.")
            .Produces<AntiforgeryTokenResource>();

        // 202, not 200: the work these two acknowledge — finding the account,
        // sending the mail — is deliberately not reported on, so "accepted" is
        // the only honest status. The rate limit is the frozen per-IP throttle.
        identity.MapPost("/password/forgot", ForgotPassword)
            .WithSummary("Begin a password reset.")
            .Produces<AcknowledgedResource>(StatusCodes.Status202Accepted)
            .RequireRateLimiting(RecoveryRateLimitPolicy);

        identity.MapPost("/password/reset", ResetPassword)
            .WithSummary("Consume a reset token and set a new password.")
            .Produces(StatusCodes.Status204NoContent)
            .RequireRateLimiting(RecoveryRateLimitPolicy);

        identity.MapPost("/username/recover", RecoverUsername)
            .WithSummary("Email the username for an account.")
            .Produces<AcknowledgedResource>(StatusCodes.Status202Accepted)
            .RequireRateLimiting(RecoveryRateLimitPolicy);
    }

    private static async Task<IResult> Register(
        RegisterRequest request,
        IDispatcher dispatcher,
        SignInManager<HsmUser> signInManager,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(signInManager);

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

        await signInManager.SignInAsync(user, isPersistent: false);

        // Location is /me rather than /api/v1/users/{id}: that collection is
        // admin-only, and a patient who has just registered would get a 403
        // following it.
        return Results.Created(
            "/api/v1/identity/me", await MeAsync(user, signInManager.UserManager));
    }

    private static async Task<IResult> Login(
        LoginRequest request,
        IDispatcher dispatcher,
        SignInManager<HsmUser> signInManager,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(signInManager);

        // The COMMAND verifies the credential (and owns lockout accounting);
        // the ENDPOINT writes the cookie. Credential checking is application
        // logic that a Blazor circuit needs too; writing a cookie is transport
        // and only this door does it.
        //
        // SignInAsync, deliberately NOT PasswordSignInAsync: the latter would
        // look the account up itself through UserManager.FindByNameAsync, which
        // does not filter soft-deleted rows and can therefore resolve a
        // different account than LoginHandler just authenticated (the filtered
        // unique indexes let a deleted row and a live row share a username).
        // This overload issues a cookie for an already-resolved user and looks
        // nothing up.
        var user = await dispatcher.Send(
            new LoginCommand(request.Username, request.Password), ct);
        await signInManager.SignInAsync(user, isPersistent: false);
        return Results.Ok(await MeAsync(user, signInManager.UserManager));
    }

    /// <summary>
    /// Sign-out. Nothing is dispatched because nothing in the application
    /// changes: the session IS the cookie, and this hands it back.
    ///
    /// <para>Unconditional, and 204 even for a caller with no session. The
    /// frozen <c>LogoutCommand</c> was <c>[AllowAnonymousRequest]</c> for the
    /// reason that still applies — sign-out must always be possible — and
    /// refusing an unauthenticated caller here would leave a browser holding a
    /// stale or unreadable cookie with no way to be rid of it, which is the one
    /// state this route exists to fix.</para>
    ///
    /// <para>What it does NOT do is revoke the cookie server-side. An Identity
    /// cookie is self-contained, so a copy taken before sign-out stays valid
    /// until it expires; the account-wide kill switch is the security stamp,
    /// which a password change rotates. Bumping it here would sign the user out
    /// of every other device too, which is not what "log out" means.</para>
    /// </summary>
    private static async Task<IResult> Logout(SignInManager<HsmUser> signInManager)
    {
        ArgumentNullException.ThrowIfNull(signInManager);
        await signInManager.SignOutAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> Me(
        IDispatcher dispatcher, UserManager<HsmUser> users, CancellationToken ct) =>
        Results.Ok(await MeAsync(await dispatcher.Send(new GetMeQuery(), ct), users));

    private static async Task<IResult> Onboarding(
        OnboardingRequest request,
        HttpContext context,
        IDispatcher dispatcher,
        SignInManager<HsmUser> signInManager,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(signInManager);

        var user = await dispatcher.Send(
            new CompleteOnboardingCommand(
                request.NewPassword, request.PhoneNumber, request.ConfirmEmail),
            ct);

        // Setting the password rotated the security stamp, so every cookie for
        // this account — the caller's included — is now stale and would be
        // refused on its next request. Every OTHER session dying is the point;
        // this one is not, and on the shell it presents as "I finished
        // onboarding and got bounced to the sign-in screen". RefreshSignInAsync
        // reissues THIS session's cookie with the new stamp, and carries the
        // now-completed onboarding claim with it.
        //
        // Only when the caller actually has a cookie session, for the same
        // reason ChangeOwnPassword guards it: a bearer caller must not be handed
        // one as a side effect.
        if ((await context.AuthenticateAsync(IdentityConstants.ApplicationScheme)).Succeeded)
        {
            await signInManager.RefreshSignInAsync(user);
        }

        return Results.Ok(await MeAsync(user, signInManager.UserManager));
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

    /// <summary>
    /// The one projection every route in this file returns. Roles come from
    /// Identity's own assignment table, which is a second read — the same one
    /// the users module pays — and it is why <see cref="MeResource.From"/>
    /// takes them rather than reading them off the entity.
    /// </summary>
    private static async Task<MeResource> MeAsync(HsmUser user, UserManager<HsmUser> users) =>
        MeResource.From(user, [.. await users.GetRolesAsync(user)]);
}
