using Hsm.Application.Abstractions;
using Hsm.Application.Auth.Commands.Login;
using Hsm.Domain.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;

namespace Hsm.Api.Identity;

/// <summary>
/// The identity resource. Task 12 opens it with the two routes the
/// authentication mechanism itself needs — start a session, and get the token
/// that proves a later mutation was not forged. Task 13 moves the rest of the
/// <c>/v1/auth</c> surface here (register, logout, me, onboarding, recovery)
/// and Task 14 adds the integration routes.
/// </summary>
public static class IdentityEndpoints
{
    public static void MapIdentityEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var identity = app.MapGroup("/api/v1/identity").WithTags("Identity");

        identity.MapPost("/login", Login)
            .WithSummary("Sign in and start a session.")
            .Produces<MeResource>();

        identity.MapGet("/csrf", Csrf)
            .WithSummary("Issue an antiforgery token.")
            .Produces<AntiforgeryTokenResource>();
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
        return Results.Ok(MeResource.From(user, [.. await signInManager.UserManager.GetRolesAsync(user)]));
    }

    private static IResult Csrf(HttpContext context, IAntiforgery antiforgery)
    {
        ArgumentNullException.ThrowIfNull(antiforgery);

        // GetAndStoreTokens writes the cookie half and hands back the half the
        // caller must echo in X-XSRF-TOKEN.
        var tokens = antiforgery.GetAndStoreTokens(context);
        return Results.Ok(new AntiforgeryTokenResource(tokens.RequestToken!));
    }
}
