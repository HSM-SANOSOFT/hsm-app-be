namespace Hsm.Application.Auth;

/// <summary>Outcome of validating a JWT: the principal, or why not.</summary>
public sealed record TokenValidation(AuthPrincipal? Principal, bool IsExpired)
{
    public static readonly TokenValidation Invalid = new(null, IsExpired: false);
    public static readonly TokenValidation Expired = new(null, IsExpired: true);
}

/// <summary>
/// Signs and validates the two JWT families (access vs refresh secret).
/// Claim layout is the adapter's business; the application deals in
/// <see cref="AuthPrincipal"/>.
/// </summary>
public interface IAuthTokenCodec
{
    string Sign(AuthPrincipal principal, TokenKind kind, TimeSpan lifetime);

    /// <summary>
    /// Validates signature (and expiry unless ignored) against the secret for
    /// <paramref name="kind"/>.
    /// </summary>
    Task<TokenValidation> ValidateAsync(string token, TokenKind kind, bool ignoreExpiration = false);
}
