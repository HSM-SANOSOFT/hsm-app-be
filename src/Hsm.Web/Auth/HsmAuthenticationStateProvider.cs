using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;

namespace Hsm.Web.Auth;

/// <summary>
/// Blazor-side authentication state. At circuit start (and on every statically
/// rendered page) the host hands this provider the HttpContext principal that
/// <see cref="HsmCookieAuthenticationHandler"/> validated from the access-token
/// cookie via <see cref="RequestAuth"/> transport — the framework delivers it
/// through <see cref="IHostEnvironmentAuthenticationStateProvider"/>, which is
/// reliable inside circuits where reaching for IHttpContextAccessor is not.
/// Components consume this only through the cascading AuthenticationState and
/// the contracts-declared UI services.
/// </summary>
public sealed class HsmAuthenticationStateProvider
    : AuthenticationStateProvider, IHostEnvironmentAuthenticationStateProvider
{
    private static readonly Task<AuthenticationState> Anonymous =
        Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));

    private Task<AuthenticationState>? _state;

    public override Task<AuthenticationState> GetAuthenticationStateAsync() => _state ?? Anonymous;

    public void SetAuthenticationState(Task<AuthenticationState> authenticationStateTask)
    {
        _state = authenticationStateTask;
        NotifyAuthenticationStateChanged(authenticationStateTask);
    }
}
