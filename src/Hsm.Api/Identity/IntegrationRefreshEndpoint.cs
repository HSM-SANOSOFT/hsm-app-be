using System.Text.Json.Serialization;
using Hsm.Application.Abstractions;
using Hsm.Application.Auth;
using Hsm.Application.Auth.Commands.RefreshTokens;
using Hsm.Application.Errors;

namespace Hsm.Api.Identity;

/// <summary>
/// The last surviving <c>/v1/auth</c> route, kept alive deliberately for one
/// more task.
///
/// <para><b>Why it is still here.</b> Task 13's brief lists
/// <c>AuthEndpoints.cs</c> as a wholesale deletion and assigns
/// <c>POST /api/v1/identity/refresh</c> to Task 14. Between those two facts
/// sits a gap: an integration account's refresh token has no other redemption
/// path, and the shell's integration-accounts screen hands operators one
/// today. Task 12 deleted this route on the same reasoning the brief uses,
/// review caught it, and it was restored on purpose — so deleting it again
/// here, one task before its replacement lands, would undo a decision rather
/// than follow a plan. It moves to <c>POST /api/v1/identity/refresh</c> over
/// an opaque token in Task 14, and this file goes with it.</para>
///
/// <para><b>What it is NOT.</b> Not a browser facility.
/// <c>RefreshTokensHandler</c> refuses any non-integration principal outright,
/// and after Task 13 no human is issued a refresh token at all — registering,
/// signing in and completing onboarding all return the user row and a session
/// cookie. Browsers hold a sliding, encrypted session; there is nothing for
/// them to refresh.</para>
/// </summary>
public static class IntegrationRefreshEndpoint
{
    public static void MapIntegrationRefreshEndpoint(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Its FROZEN path and method, unchanged: moving it is Task 14's job,
        // and moving it twice would break the same client twice.
        app.MapGet("/v1/auth/refresh", Refresh)
            .WithTags("Identity")
            .WithSummary("Rotate an integration account's token pair (retired in Task 14).")
            .Produces<TokenPairResource>();
    }

    private static async Task<IResult> Refresh(HttpContext context, IDispatcher dispatcher)
    {
        // Read from the Authorization header ONLY. The token is presented
        // explicitly here, never ambiently — which is also why the adaptive
        // scheme's bearer handler cannot have authenticated this request: a
        // refresh token is signed with the refresh secret, so HttpContext.User
        // is anonymous and RefreshTokensCommand's [AllowAnonymousRequest] is
        // what lets it through. The TOKEN is the credential, and the two checks
        // that verify it — signature/expiry here, stored hash in the handler —
        // both still run.
        var rawToken = Bearer(context);
        if (string.IsNullOrEmpty(rawToken))
        {
            throw new UnauthorizedException();
        }

        var codec = context.RequestServices.GetRequiredService<IAuthTokenCodec>();
        var validation = await codec.ValidateAsync(rawToken, TokenKind.Refresh);
        var principal = validation.Principal
            ?? throw new UnauthorizedException(validation.IsExpired ? "token expired" : "Invalid token");

        var tokens = await dispatcher.Send(
            new RefreshTokensCommand(principal, rawToken), context.RequestAborted);

        // The frozen { metadata, data } envelope is gone with the rest of the
        // retired /v1/auth surface; the token pair IS the response now. The
        // member names stay frozen: the credential's own shape is what an
        // integration parses.
        return Results.Ok(new TokenPairResource(tokens.AccessToken, tokens.RefreshToken));
    }

    /// <summary>The raw <c>Authorization: Bearer</c> value, or null.</summary>
    private static string? Bearer(HttpContext context)
    {
        var authorization = context.Request.Headers.Authorization.ToString();
        return authorization.StartsWith("Bearer ", StringComparison.Ordinal)
            ? authorization["Bearer ".Length..].Trim()
            : null;
    }
}

/// <summary>An integration's rotated credential. Retired with Task 14's opaque token.</summary>
public sealed record TokenPairResource(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string RefreshToken);
