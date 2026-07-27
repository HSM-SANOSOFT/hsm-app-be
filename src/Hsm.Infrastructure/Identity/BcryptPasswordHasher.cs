using Hsm.Application.Auth;

namespace Hsm.Infrastructure.Identity;

/// <summary>
/// Bcrypt with the frozen work factor (10 — auth.service.ts hashData).
/// </summary>
public sealed class BcryptPasswordHasher : IPasswordHasher
{
    /// <summary>Frozen salt rounds.</summary>
    public const int WorkFactor = 10;

    public string Hash(string plaintext) => BCrypt.Net.BCrypt.HashPassword(plaintext, WorkFactor);

    public bool Verify(string plaintext, string hash)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(plaintext, hash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return false;
        }
    }
}
