using Hsm.Application.Abstractions;
using Hsm.Application.Errors;

namespace Hsm.Application.Auth.Commands.RefreshTokens;

public sealed class RefreshTokensHandler(
    IUserRefreshTokenStore userTokens,
    IIntegrationRefreshTokenStore integrationTokens,
    IPasswordHasher hasher,
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

        // Pre-digest before bcrypt — see TokenDigests. Without it bcrypt would
        // compare only the first 72 bytes, which two JWTs for the same subject
        // share, and every prior token would still verify.
        if (!hasher.Verify(TokenDigests.Sha256Hex(request.RawRefreshToken), activeHash))
        {
            throw new UnauthorizedException("Refresh token is not valid");
        }

        // Reissue from the token's own claims, minus iat/exp.
        var toSign = principal with { IssuedAt = null, ExpiresAt = null };
        return await issuer.IssueAsync(toSign, ct);
    }
}
