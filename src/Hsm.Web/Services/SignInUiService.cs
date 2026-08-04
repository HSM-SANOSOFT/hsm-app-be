using Hsm.Application.Abstractions;
using Hsm.Application.Auth.Commands.Login;
using Hsm.Application.Errors;
using Hsm.Contracts.Ui;
using Hsm.Domain.Identity;
using Microsoft.AspNetCore.Identity;

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
/// <para><see cref="SignInManager{TUser}.SignInAsync(TUser, bool, string)"/>,
/// not <c>PasswordSignInAsync</c>: the latter would look the account up again
/// through the unfiltered <c>UserManager.FindByNameAsync</c> and could resolve
/// a soft-deleted row that shares the username with the live account the
/// command just authenticated.</para>
///
/// No actor is published here: signing in is the one operation whose caller
/// has no principal yet, which is why <see cref="LoginCommand"/> is
/// [AllowAnonymousRequest].
/// </summary>
public sealed class SignInUiService(
    IDispatcher dispatcher,
    SignInManager<HsmUser> signInManager) : ISignInUiService
{
    public async Task<SignInResult> SignInAsync(
        string username, string password, CancellationToken cancellationToken = default)
    {
        try
        {
            var user = await dispatcher.Send(new LoginCommand(username, password), cancellationToken);
            await signInManager.SignInAsync(user, isPersistent: false);
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
