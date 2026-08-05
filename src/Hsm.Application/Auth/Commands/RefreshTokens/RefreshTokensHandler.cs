using System.Security.Cryptography;
using System.Text;
using Hsm.Application.Abstractions;
using Hsm.Application.Errors;

namespace Hsm.Application.Auth.Commands.RefreshTokens;

public sealed class RefreshTokensHandler(
    IIntegrationRefreshTokenStore integrationTokens,
    TokenIssuer issuer)
    : IRequestHandler<RefreshTokensCommand, TokenPair>
{
    public async Task<TokenPair> HandleAsync(RefreshTokensCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var principal = request.Principal;

        // INTEGRATIONS ONLY, and this is the load-bearing line of the handler.
        //
        // Rotation re-signs the PRESENTED TOKEN'S OWN CLAIMS (see the last two
        // lines): it reads no user row, re-reads no roles and checks no security
        // stamp. For an integration that is fine — such an account's identity is
        // its id plus the single `integration` role, and revocation is the
        // stored hash compared below. For a HUMAN it is a hole: staff onboarding
        // and public signup both still hand out a refresh token, so a user
        // demoted from doctor to nurse — whose cookie session ChangeUserRole
        // correctly revokes through the security stamp — could otherwise present
        // that refresh token, receive a fresh access token still claiming
        // `doctor`, and renew it indefinitely, because every rotation issues
        // another one.
        //
        // Browsers hold a sliding, encrypted session and have no refresh token
        // to present, so nothing legitimate is refused here. It is 403 rather
        // than 401 deliberately: the credential IS valid and was verified, so
        // "authenticate again" would be a lie and would send a client into a
        // retry loop on a route that can never serve it.
        if (!principal.IsIntegration)
        {
            throw new ForbiddenException("Refresh tokens are redeemable by integration accounts only.");
        }

        var id = Guid.Parse(principal.Id);
        var activeHash = (await integrationTokens.FindActiveAsync(id, ct))?.TokenHash;
        if (activeHash is null)
        {
            throw new UnauthorizedException("Active Refresh token not found");
        }

        // Fixed-time comparison of two SHA-256 hex digests — see
        // TokenIssuer.HashRefreshToken for why there is no work factor here.
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(activeHash),
                Encoding.UTF8.GetBytes(TokenIssuer.HashRefreshToken(request.RawRefreshToken))))
        {
            throw new UnauthorizedException("Refresh token is not valid");
        }

        // Reissue from the token's own claims, minus iat/exp.
        var toSign = principal with { IssuedAt = null, ExpiresAt = null };
        return await issuer.IssueAsync(toSign, ct);
    }
}
