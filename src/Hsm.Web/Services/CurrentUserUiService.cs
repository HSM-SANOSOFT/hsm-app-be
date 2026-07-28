using System.Security.Claims;
using Hsm.Contracts.Ui;
using Microsoft.AspNetCore.Components.Authorization;

namespace Hsm.Web.Services;

/// <summary>
/// Host-side implementation of the contracts-declared identity service: the
/// shell's navigation and top bar ask this, and it answers from the session's
/// authentication state (the RequestAuth-validated principal). A WebAssembly
/// host would implement the same interface with an HTTP call.
/// </summary>
public sealed class CurrentUserUiService(AuthenticationStateProvider authenticationState)
    : ICurrentUserUiService
{
    public async Task<CurrentUserDto?> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        var state = await authenticationState.GetAuthenticationStateAsync();
        var user = state.User;
        if (user.Identity is not { IsAuthenticated: true } identity)
        {
            return null;
        }

        var id = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var roles = user.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToArray();
        return new CurrentUserDto(id, identity.Name ?? id, roles);
    }
}
