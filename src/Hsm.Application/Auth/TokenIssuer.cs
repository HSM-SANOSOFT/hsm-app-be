using Hsm.Application.Errors;
using Hsm.Domain.Identity;

namespace Hsm.Application.Auth;

/// <summary>
/// Issues token pairs and rotates the persisted refresh-token hash — the
/// shared core of login/signup/refresh/onboarding (frozen
/// AuthService.generateTokens + refreshToken).
/// </summary>
public sealed class TokenIssuer(
    IAuthTokenCodec codec,
    IPasswordHasher hasher,
    IUserRefreshTokenStore userTokens,
    IIntegrationRefreshTokenStore integrationTokens,
    IAuthUnitOfWork unitOfWork,
    IEnvironmentPolicy environment)
{
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
    /// Rotation: deactivate any active row for the principal and persist the
    /// bcrypt hash of the new refresh token, atomically. Routes to the store
    /// matching the principal's mode — the stores are never mixed.
    /// </summary>
    private async Task RotateRefreshTokenAsync(AuthPrincipal principal, string refreshToken, CancellationToken ct = default)
    {
        // Pre-digest before bcrypt — see TokenDigests.
        var hash = hasher.Hash(TokenDigests.Sha256Hex(refreshToken));
        var id = Guid.Parse(principal.Id);
        await unitOfWork.ExecuteInTransactionAsync(
            async innerCt =>
            {
                if (principal.IsIntegration)
                {
                    await integrationTokens.DeactivateActiveAsync(id, innerCt);
                    await integrationTokens.AddAsync(id, hash, innerCt);
                }
                else
                {
                    await userTokens.DeactivateActiveAsync(id, innerCt);
                    await userTokens.AddAsync(id, hash, innerCt);
                }

                await unitOfWork.SaveChangesAsync(innerCt);
                return true;
            },
            ct);
    }

    /// <summary>Generate a pair and rotate the stored hash — the login path.</summary>
    public async Task<TokenPair> IssueAsync(AuthPrincipal principal, CancellationToken ct = default)
    {
        var tokens = GenerateTokens(principal);
        await RotateRefreshTokenAsync(principal, tokens.RefreshToken, ct);
        return tokens;
    }

    /// <summary>The frozen serializeOnboarding: ISO string or null.</summary>
    public static string? SerializeOnboarding(DateTimeOffset? value) => IsoTimestamp.Of(value);

    /// <summary>Builds the JWT principal for a user row (roles flattened).</summary>
    public static AuthPrincipal PrincipalFor(User user) => new()
    {
        Id = user.Id.ToString(),
        Username = user.Username,
        Email = user.Email,
        FirstName = user.FirstName,
        FirstLastName = user.FirstLastName,
        Roles = user.Roles.Select(r => r.Role).ToList(),
        OnboardingCompletedAt = SerializeOnboarding(user.OnboardingCompletedAt),
        HasOnboardingClaim = true,
    };
}
