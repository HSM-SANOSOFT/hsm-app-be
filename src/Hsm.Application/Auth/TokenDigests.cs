using System.Security.Cryptography;
using System.Text;

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
