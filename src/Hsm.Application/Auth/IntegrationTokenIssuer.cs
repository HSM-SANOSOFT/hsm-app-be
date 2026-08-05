using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Hsm.Application.Errors;
using Hsm.Domain.Identity;

namespace Hsm.Application.Auth;

/// <summary>
/// Mints an integration account's credential and rotates the stored refresh
/// digest — the shared core of provisioning, admin re-issue and refresh.
///
/// <para><b>Two different kinds of secret.</b> The ACCESS token stays a signed
/// JWT: something has to read it, and the bearer handler that does is the
/// framework's. The REFRESH token stopped being one. A refresh token is
/// presented to exactly one route, which does exactly one thing with it — look
/// for a row — so every claim a JWT would carry is a claim nobody reads and a
/// disclosure nobody needs.</para>
///
/// <para>Humans do not pass through here at all. Registering, signing in and
/// completing onboarding return the user row and the door writes an Identity
/// session cookie; there is no human bearer token to issue and no human refresh
/// token to rotate.</para>
/// </summary>
public sealed class IntegrationTokenIssuer(
    IIntegrationTokenCodec codec,
    IIntegrationRefreshTokenStore refreshTokens,
    IAuthUnitOfWork unitOfWork,
    IEnvironmentPolicy environment)
{
    /// <summary>
    /// How long an integration's access token lives. Stated once: the signer
    /// below uses it, and the register/refresh routes report it to the caller
    /// as <c>expiresInSeconds</c> so a client never has to decode a credential
    /// to learn when to renew it.
    /// </summary>
    public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromDays(1);

    /// <summary>
    /// 256 bits of entropy, URL-safe. It is opaque on purpose: a refresh token
    /// carries no claims a reader could act on, so there is nothing to validate
    /// and nothing to leak — its only property is that it matches a stored row.
    /// </summary>
    private static string NewRefreshToken() =>
        Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    /// <summary>
    /// SHA-256, not bcrypt. bcrypt exists to make a LOW-entropy secret expensive
    /// to guess; a 256-bit random value is not guessable, so the work factor
    /// buys nothing and costs ~100 ms of held connection on every refresh. The
    /// frozen stack's SHA-256 pre-digest existed only because bcrypt silently
    /// truncates past 72 bytes — with bcrypt gone, so is the reason for the
    /// pre-digest, and the digest IS the stored value rather than a step on the
    /// way to one.
    /// </summary>
    public static string HashRefreshToken(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>
    /// Signs an access token and mints a fresh opaque refresh token, replacing
    /// whatever the account held before.
    ///
    /// <para>Integrations only, and the refusal is here rather than at a call
    /// site so no future one can miss it: there is no store to rotate a human's
    /// token in, and silently skipping the rotation would hand out a refresh
    /// token that nothing could ever revoke.</para>
    ///
    /// <para>Deactivate-then-insert runs through
    /// <see cref="IAuthUnitOfWork.ExecuteInTransactionAsync"/>, which joins the
    /// transaction <c>TransactionBehavior</c> already opened for the command.
    /// The two writes therefore commit together: an account can never be left
    /// with its old token revoked and no new one persisted.</para>
    /// </summary>
    public async Task<IntegrationTokens> IssueAsync(AuthPrincipal principal, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(principal);
        if (!principal.IsIntegration)
        {
            throw new ForbiddenException("Token pairs are issued to integration accounts only.");
        }

        // The frozen env gate, moved across unchanged.
        if (principal.Roles.Contains(Roles.Developer) && !environment.IsDev)
        {
            throw new ForbiddenException("Developer role cannot be assigned in this environment");
        }

        var accountId = Guid.Parse(principal.Id);
        var accessToken = codec.Sign(principal, AccessTokenLifetime);
        var refreshToken = NewRefreshToken();
        var hash = HashRefreshToken(refreshToken);

        await unitOfWork.ExecuteInTransactionAsync(
            async innerCt =>
            {
                await refreshTokens.DeactivateActiveAsync(accountId, innerCt);
                await refreshTokens.AddAsync(accountId, hash, innerCt);
                await unitOfWork.SaveChangesAsync(innerCt);
                return true;
            },
            ct);

        return new IntegrationTokens(accountId, accessToken, refreshToken);
    }
}
