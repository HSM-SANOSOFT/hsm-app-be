using System.Security.Claims;
using Hsm.Application.Abstractions;
using Hsm.Application.Auth;
using Microsoft.AspNetCore.Components.Authorization;

namespace Hsm.Web.Auth;

/// <summary>
/// Publishes the Blazor session's actor for the pipeline, replacing the
/// deleted <c>UiServiceGate</c>. The gate used to do two jobs — decide
/// authorization AND supply the actor. Only the second job was ever the
/// host's: authorization is a property of the request, declared on the request
/// type and enforced by <c>AuthorizationBehavior</c>, so what is left here
/// installs an identity and decides nothing.
///
/// This deliberately does not throw for an anonymous circuit. Installing
/// nothing leaves <see cref="ICurrentPrincipal.Actor"/> null, and the pipeline
/// answers with the same 401 an unauthenticated HTTP request gets — one
/// refusal, one place.
///
/// The authentication state (rather than <c>IHttpContextAccessor</c>) is the
/// source because it is the only one that resolves both inside an interactive
/// circuit and during static server rendering; see
/// <see cref="HsmAuthenticationStateProvider"/>.
/// </summary>
public sealed class ShellActor(
    AuthenticationStateProvider authenticationState,
    AmbientPrincipal ambientPrincipal,
    RequestActorFactory actorFactory)
{
    /// <summary>
    /// Derives the actor from the circuit's authentication state and publishes
    /// it for this scope. Returns the installed actor, or <see langword="null"/>
    /// when the session is anonymous or unidentifiable.
    /// </summary>
    public async Task<RequestActor?> InstallAsync(CancellationToken ct = default)
    {
        var state = await authenticationState.GetAuthenticationStateAsync();
        var user = state.User;
        var id = user.Identity is { IsAuthenticated: true }
            ? user.FindFirstValue(ClaimTypes.NameIdentifier)
            : null;
        if (string.IsNullOrEmpty(id))
        {
            ambientPrincipal.Set(null);
            return null;
        }

        var actor = await actorFactory.CreateAsync(
            id,
            [.. user.FindAll(ClaimTypes.Role).Select(claim => claim.Value)],
            user.FindFirstValue(AuthPrincipalClaims.OnboardingCompletedAtClaim),
            ct);
        ambientPrincipal.Set(actor);
        return actor;
    }
}
