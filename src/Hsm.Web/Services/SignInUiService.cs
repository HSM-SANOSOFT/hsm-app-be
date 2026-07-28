using Hsm.Application.Auth;
using Hsm.Application.Errors;
using Hsm.Contracts.Ui;
using Hsm.Web.Auth;

namespace Hsm.Web.Services;

/// <summary>
/// Host-side sign-in (plan U18, screen 1): validates credentials through the
/// in-process <see cref="LoginHandler"/> and sets THE SAME auth cookies the
/// REST login sets, via the shared <see cref="AuthCookies"/> plumbing. The
/// sign-in page renders in static SSR specifically so this service runs
/// during a live HTTP response — cookies cannot be set from a circuit.
/// </summary>
public sealed class SignInUiService(
    LoginHandler handler,
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
            var principal = await handler.ValidateCredentialsAsync(username, password, cancellationToken);
            var tokens = await handler.IssueAsync(principal, cancellationToken);
            AuthCookies.Set(ctx, options, tokens);
            return SignInResult.Success;
        }
        catch (ApiException)
        {
            // Unknown username and wrong password surface the same message —
            // the screen must not leak which accounts exist (frozen posture).
            return SignInResult.Failed("Usuario o contraseña incorrectos.");
        }
    }
}
