using System.Text.Encodings.Web;
using Hsm.Application.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Hsm.Web.Auth;

/// <summary>
/// The shell's authentication scheme: validates the access-token cookie (or
/// bearer header) through the same token transport and codec the REST surface
/// uses (<see cref="RequestAuth"/>), so a browser session signs in exactly
/// once, via <c>POST /v1/auth/login</c>. Only Blazor page endpoints carry
/// authorization metadata — the /v1 API keeps its own frozen guard chain and
/// never challenges. A failed or absent session on a protected page redirects
/// to the sign-in page.
/// </summary>
public sealed class HsmCookieAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IAuthTokenCodec codec)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "HsmCookie";

    /// <summary>The sign-in page unauthenticated visitors are sent to.</summary>
    public const string LoginPath = "/login";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var raw = RequestAuth.AccessToken(Context);
        if (string.IsNullOrEmpty(raw))
        {
            return AuthenticateResult.NoResult();
        }

        var validation = await codec.ValidateAsync(raw, TokenKind.Access);
        if (validation.Principal is null)
        {
            // Expired or invalid tokens leave the visitor anonymous; the
            // challenge below routes them to sign-in.
            return AuthenticateResult.NoResult();
        }

        return AuthenticateResult.Success(
            new AuthenticationTicket(validation.Principal.ToClaimsPrincipal(), SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var returnUrl = Request.Path + Request.QueryString;
        Response.Redirect($"{LoginPath}?returnUrl={Uri.EscapeDataString(returnUrl)}");
        return Task.CompletedTask;
    }
}
