using System.Security.Cryptography;
using System.Text;
using Hsm.Application.Errors;
using Hsm.Domain.Identity;

namespace Hsm.Application.Auth;

/// <summary>
/// SHA-256 pre-digest for tokens that are bcrypt-hashed at rest. Bcrypt only
/// reads the first 72 bytes of its input, and two JWTs for the same subject
/// share their first 72 bytes — hashing the raw JWT would make every token
/// for a user verify against every other one, silently defeating refresh
/// rotation (a latent defect in the frozen implementation; DoD C1 requires
/// the prior token to be rejected, so it is fixed here deliberately).
/// </summary>
public static class TokenDigests
{
    public static string Sha256Hex(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

/// <summary>
/// Frozen account-recovery thresholds (account-recovery.service.ts): reset
/// tokens are 256-bit, SHA-256 hashed at rest, expire after ONE HOUR, are
/// single-use; at most FIVE reset requests per account per rolling hour.
/// These values are behavior, pinned by contract tests.
/// </summary>
public static class RecoveryPolicy
{
    public static readonly TimeSpan TokenTtl = TimeSpan.FromHours(1);
    public const int MaxRequestsPerHour = 5;
}

/// <summary>
/// Begin a password reset. Silent no-op for unknown accounts (non-enumerating);
/// the ONLY surfaced failure is the per-account rate limit (429).
/// </summary>
public sealed class ForgotPasswordHandler(
    IUserStore users,
    IPasswordResetTokenStore resetTokens,
    IRecoveryEmailer emailer,
    IAuthUnitOfWork unitOfWork)
{
    public async Task HandleAsync(string email, CancellationToken ct = default)
    {
        var user = await users.FindActiveByEmailAsync(email, ct);
        if (user is null)
        {
            return; // Non-enumerating: nothing observable.
        }

        var since = DateTimeOffset.UtcNow - RecoveryPolicy.TokenTtl;
        var recent = await resetTokens.CountForUserSinceAsync(user.Id, since, ct);
        if (recent >= RecoveryPolicy.MaxRequestsPerHour)
        {
            throw ApiException.TooManyRequests();
        }

        var plaintext = RandomNumberGenerator.GetHexString(64, lowercase: true);
        var tokenHash = TokenDigests.Sha256Hex(plaintext);
        await resetTokens.AddAsync(
            new PasswordResetToken
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                TokenHash = tokenHash,
                ExpiresAt = DateTimeOffset.UtcNow + RecoveryPolicy.TokenTtl,
                UsedAt = null,
            },
            ct);
        await unitOfWork.SaveChangesAsync(ct);

        try
        {
            // The plaintext token is handed to the mailer and NEVER persisted.
            await emailer.SendPasswordResetAsync(user.Email, plaintext, ct);
        }
        catch (Exception)
        {
            // Compensate: drop the unusable token so it doesn't burn a
            // rate-limit slot, and stay generic — surfacing a 500 only for
            // known accounts would break non-enumeration.
            await resetTokens.DeleteByHashAsync(tokenHash, ct);
            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}

/// <summary>
/// Consume a reset token and set a new password. Invalid, expired, and
/// already-used tokens all fail with the SAME generic message; consumption is
/// atomic and single-use; active sessions are revoked with the change.
/// </summary>
public sealed class ResetPasswordHandler(
    IUserStore users,
    IPasswordResetTokenStore resetTokens,
    IUserRefreshTokenStore userTokens,
    IPasswordHasher hasher,
    IAuthUnitOfWork unitOfWork)
{
    private const string GenericFailure = "Invalid or expired reset token";

    public async Task HandleAsync(string token, string newPassword, CancellationToken ct = default)
    {
        var row = await resetTokens.FindByHashAsync(TokenDigests.Sha256Hex(token), ct);
        if (row is null || row.UsedAt is not null || row.ExpiresAt < DateTimeOffset.UtcNow)
        {
            throw ApiException.BadRequest(GenericFailure);
        }

        var passwordHash = hasher.Hash(newPassword);

        await unitOfWork.ExecuteInTransactionAsync(
            async innerCt =>
            {
                // Consume FIRST, conditionally on still-unused: two racing
                // requests can never both apply a password.
                if (!await resetTokens.TryConsumeAsync(row.Id, innerCt))
                {
                    throw ApiException.BadRequest(GenericFailure);
                }

                await users.UpdatePasswordAsync(row.UserId, passwordHash, innerCt);
                // Revoke sessions so a pre-reset stolen session cannot outlive
                // the password change.
                await userTokens.DeactivateActiveAsync(row.UserId, innerCt);
                await unitOfWork.SaveChangesAsync(innerCt);
                return true;
            },
            ct);
    }
}

/// <summary>Email the username for an account. Silent no-op when unknown.</summary>
public sealed class RecoverUsernameHandler(IUserStore users, IRecoveryEmailer emailer)
{
    public async Task HandleAsync(string email, CancellationToken ct = default)
    {
        var user = await users.FindActiveByEmailAsync(email, ct);
        if (user is null)
        {
            return; // Non-enumerating: nothing observable.
        }

        try
        {
            await emailer.SendUsernameReminderAsync(email, user.Username, ct);
        }
        catch (Exception)
        {
            // Swallow: a known account must not 500 while an unknown one
            // returns the generic outcome.
        }
    }
}
