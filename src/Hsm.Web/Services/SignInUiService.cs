using Hsm.Application.Abstractions;
using Hsm.Application.Auth.Commands.Login;
using Hsm.Application.Errors;
using Hsm.Contracts.Ui;
using Hsm.Domain.Identity;
using Hsm.Infrastructure.Identity;

// Identity publishes a SignInResult of its own; the shell's screen contract is
// Hsm.Contracts', and only that one is ever returned here.
using SignInResult = Hsm.Contracts.Ui.SignInResult;

namespace Hsm.Web.Services;

/// <summary>
/// Host-side sign-in (plan U18, screen 1): dispatches the same
/// <see cref="LoginCommand"/> the REST route dispatches — so credential
/// checking, the enumeration-safe refusal and lockout accounting are one
/// implementation — and then writes THE SAME Identity session cookie, through
/// <see cref="SignInManager{TUser}"/>. The sign-in page renders in static SSR
/// specifically so this service runs during a live HTTP response; a cookie
/// cannot be set from a circuit.
///
/// <para>Through <see cref="HsmSessionSignIn"/> rather than
/// <c>SignInManager</c> directly, and that is not a detail: a session is a
/// cookie AND a server-side row, and a cookie minted without its row is refused
/// on the very next request. Going through the same seam the REST door uses is
/// what keeps the shell's sessions as revocable as the API's.</para>
///
/// No actor is published here: signing in is the one operation whose caller
/// has no principal yet, which is why <see cref="LoginCommand"/> is
/// [AllowAnonymousRequest].
/// </summary>
public sealed class SignInUiService(
    IDispatcher dispatcher,
    HsmSessionSignIn sessions) : ISignInUiService
{
    public async Task<SignInResult> SignInAsync(
        string username, string password, CancellationToken cancellationToken = default)
    {
        try
        {
            var user = await dispatcher.Send(new LoginCommand(username, password), cancellationToken);
            await sessions.SignInAsync(user, cancellationToken);
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
