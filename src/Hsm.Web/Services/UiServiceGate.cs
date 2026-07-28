using System.Security.Claims;
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
public sealed class UiServiceGate(AuthenticationStateProvider authenticationState)
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

        return user;
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
