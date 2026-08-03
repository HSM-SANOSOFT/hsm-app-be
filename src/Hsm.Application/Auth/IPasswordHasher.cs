namespace Hsm.Application.Auth;

/// <summary>Bcrypt in the adapter; the application sees only hash/verify.</summary>
public interface IPasswordHasher
{
    string Hash(string plaintext);
    bool Verify(string plaintext, string hash);
}
