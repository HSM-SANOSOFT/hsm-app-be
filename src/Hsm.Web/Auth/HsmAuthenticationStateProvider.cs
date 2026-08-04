using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;

namespace Hsm.Web.Auth;

/// <summary>
/// Blazor-side authentication state. At circuit start (and on every statically
/// rendered page) the host hands this provider the HttpContext principal the
/// Identity application cookie produced — the framework delivers it through
/// <see cref="IHostEnvironmentAuthenticationStateProvider"/>, which is reliable
/// inside circuits where reaching for IHttpContextAccessor is not. Components
/// consume this only through the cascading AuthenticationState and the
/// contracts-declared UI services.
///
/// <para>This is <c>ServerAuthenticationStateProvider</c> in all but one
/// respect, and that respect is why it is not simply that type: the framework
/// one THROWS when asked for state that was never set. An anonymous in-process
/// dispatch has no state, and it must reach the pipeline and be refused there
/// with a 401 — not die with an InvalidOperationException in the provider.
/// Anonymous is the answer, not an error.</para>
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
