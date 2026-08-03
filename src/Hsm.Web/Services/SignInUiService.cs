using Hsm.Application.Abstractions;
using Hsm.Application.Auth.Commands.Login;
using Hsm.Application.Errors;
using Hsm.Contracts.Ui;
using Hsm.Web.Auth;

namespace Hsm.Web.Services;

/// <summary>
/// Host-side sign-in (plan U18, screen 1): dispatches the same
/// <see cref="LoginCommand"/> the REST route dispatches and sets THE SAME auth
/// cookies, via the shared <see cref="AuthCookies"/> plumbing. The sign-in page
/// renders in static SSR specifically so this service runs during a live HTTP
/// response — cookies cannot be set from a circuit.
///
/// No actor is published here: signing in is the one operation whose caller
/// has no principal yet, which is why <see cref="LoginCommand"/> is
/// [AllowAnonymousRequest].
/// </summary>
public sealed class SignInUiService(
    IDispatcher dispatcher,
    IHttpContextAccessor httpContextAccessor,
    AuthWebOptions options) : ISignInUiService
{
    public async Task<SignInResult> SignInAsync(
        string username, string password, CancellationToken cancellationToken = default)
    {
        var ctx = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException(
                "Sign-in requires a live HTTP response (static SSR); it cannot run inside a circuit.");

        try
        {
            var tokens = await dispatcher.Send(new LoginCommand(username, password), cancellationToken);
            AuthCookies.Set(ctx, options, tokens);
            return SignInResult.Success;
        }
        catch (HsmException)
        {
            // Unknown username and wrong password surface the same message —
            // the screen must not leak which accounts exist (frozen posture).
            return SignInResult.Failed("Usuario o contraseña incorrectos.");
        }
    }
}
