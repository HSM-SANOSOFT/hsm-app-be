using System.Security.Claims;
using Hsm.Application.Abstractions;
using Hsm.Domain.Identity;
using Microsoft.AspNetCore.Components.Authorization;

namespace Hsm.Web.Services;

/// <summary>
/// Role enforcement for the admin UI services (plan U18). These services run
/// in process — no endpoint guard sits in front of them — so each call
/// re-checks the authenticated principal, applying the equivalent of the
/// REST surface's admin gate (RequestAuth.GateAsync with the admin role).
/// The principal comes from the same validated session state the shell's
/// authentication scheme established; admins are exempt from the onboarding
/// gate by the frozen rules, so admin-role membership is the whole check.
/// </summary>
public sealed class UiServiceGate(
    AuthenticationStateProvider authenticationState,
    AmbientPrincipal ambientPrincipal)
{
    /// <summary>The signed-in admin, or throws — never a silent pass.</summary>
    public async Task<ClaimsPrincipal> RequireAdminAsync()
    {
        var state = await authenticationState.GetAuthenticationStateAsync();
        var user = state.User;
        if (user.Identity is not { IsAuthenticated: true })
        {
            throw new UnauthorizedAccessException("No hay una sesión activa.");
        }

        if (!user.IsInRole(Roles.Admin))
        {
            throw new UnauthorizedAccessException("Se requiere el rol de administrador.");
        }

        InstallActor(user);
        return user;
    }

    /// <summary>
    /// Publishes the circuit's principal as the actor the application pipeline
    /// authorizes against, so an in-process dispatch is gated by the same
    /// RequireRole policy an HTTP dispatch is. Onboarding is recorded as
    /// satisfied because this gate has just established the caller is an admin,
    /// and the frozen OnboardingGuard exempts admins outright.
    /// </summary>
    private void InstallActor(ClaimsPrincipal user)
    {
        var id = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException("La sesión no identifica al usuario.");
        var roles = user.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToList();
        ambientPrincipal.Set(new RequestActor(id, roles, OnboardingCompleted: true));
    }

    /// <summary>The signed-in admin's user id.</summary>
    public async Task<Guid> RequireAdminIdAsync()
    {
        var user = await RequireAdminAsync();
        var id = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException("La sesión no identifica al usuario.");
        return Guid.Parse(id);
    }
}
