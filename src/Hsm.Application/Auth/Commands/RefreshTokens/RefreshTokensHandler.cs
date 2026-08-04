using System.Security.Cryptography;
using System.Text;
using Hsm.Application.Abstractions;
using Hsm.Application.Errors;

namespace Hsm.Application.Auth.Commands.RefreshTokens;

public sealed class RefreshTokensHandler(
    IUserRefreshTokenStore userTokens,
    IIntegrationRefreshTokenStore integrationTokens,
    TokenIssuer issuer)
    : IRequestHandler<RefreshTokensCommand, TokenPair>
{
    public async Task<TokenPair> HandleAsync(RefreshTokensCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var principal = request.Principal;
        var id = Guid.Parse(principal.Id);
        string? activeHash = principal.IsIntegration
            ? (await integrationTokens.FindActiveAsync(id, ct))?.TokenHash
            : (await userTokens.FindActiveAsync(id, ct))?.TokenHash;

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
