using System.Security.Cryptography;
using System.Text;
using Hsm.Application.Errors;
using Hsm.Domain.Identity;

namespace Hsm.Application.Auth;

/// <summary>
/// Issues token pairs and rotates the persisted refresh-token hash for
/// INTEGRATION accounts — the shared core of integration provisioning,
/// re-issue and refresh rotation.
///
/// <para>Humans no longer pass through here at all. Registering, signing in
/// and completing onboarding all return the user row now and the door writes
/// an Identity session cookie, so the user half of the frozen token story —
/// <c>UserRefreshToken</c> and its store — has no writer left and was deleted
/// with this task. Task 14 replaces the remaining JWT pair with an opaque
/// integration token and this type goes with it.</para>
/// </summary>
public sealed class TokenIssuer(
    IAuthTokenCodec codec,
    IIntegrationRefreshTokenStore integrationTokens,
    IAuthUnitOfWork unitOfWork,
    IEnvironmentPolicy environment)
{
    /// <summary>
    /// SHA-256, not bcrypt. bcrypt exists to make a LOW-entropy secret
    /// expensive to guess; a signed refresh JWT is not guessable, so the work
    /// factor buys nothing and costs ~100 ms of held connection on every
    /// issue. The frozen stack's SHA-256 pre-digest existed only because
    /// bcrypt silently truncates past 72 bytes — with bcrypt gone, the digest
    /// IS the stored value rather than a step on the way to one.
    /// </summary>
    public static string HashRefreshToken(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>
    /// Signs a fresh access/refresh pair. Developer-role principals are
    /// rejected outside dev (frozen env gate).
    /// </summary>
    public TokenPair GenerateTokens(AuthPrincipal principal)
    {
        if (principal.Roles.Contains(Roles.Developer) && !environment.IsDev)
        {
            throw new ForbiddenException("Developer role cannot be assigned in this environment");
        }

        var integration = principal.IsIntegration;
        var access = codec.Sign(
            principal,
            TokenKind.Access,
            integration ? TokenLifetimes.IntegrationAccess : TokenLifetimes.UserAccess);
        var refresh = codec.Sign(
            principal,
            TokenKind.Refresh,
            integration ? TokenLifetimes.IntegrationRefresh : TokenLifetimes.UserRefresh);
        return new TokenPair(access, refresh);
    }

    /// <summary>
    /// Rotation: deactivate any active row for the account and persist the hash
    /// of the new refresh token, atomically. Exactly one row is active per
    /// integration account, which is what makes the previous token stop working.
    /// </summary>
    private async Task RotateRefreshTokenAsync(AuthPrincipal principal, string refreshToken, CancellationToken ct = default)
    {
        var hash = HashRefreshToken(refreshToken);
        var id = Guid.Parse(principal.Id);
        await unitOfWork.ExecuteInTransactionAsync(
            async innerCt =>
            {
                await integrationTokens.DeactivateActiveAsync(id, innerCt);
                await integrationTokens.AddAsync(id, hash, innerCt);
                await unitOfWork.SaveChangesAsync(innerCt);
                return true;
            },
            ct);
    }

    /// <summary>
    /// Generate a pair and rotate the stored hash.
    ///
    /// <para>Integrations only, and the refusal is here rather than at a call
    /// site so no future one can miss it: there is no store to rotate a human's
    /// token in, and silently skipping the rotation would hand out a refresh
    /// token that nothing could ever revoke.</para>
    /// </summary>
    public async Task<TokenPair> IssueAsync(AuthPrincipal principal, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(principal);
        if (!principal.IsIntegration)
        {
            throw new ForbiddenException("Token pairs are issued to integration accounts only.");
        }

        var tokens = GenerateTokens(principal);
        await RotateRefreshTokenAsync(principal, tokens.RefreshToken, ct);
        return tokens;
    }

    // PrincipalFor(HsmUser, roles) and SerializeOnboarding lived here to build
    // a JWT principal out of a user ROW. Nothing does that any more — a human's
    // claims are minted by HsmUserClaimsPrincipalFactory into the session
    // cookie, and this type only ever sees integration principals — so both
    // went with the user refresh token rather than being left as a
    // ready-to-hand way to sign a person a bearer token.
}
