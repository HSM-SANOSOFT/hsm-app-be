namespace Hsm.Application.Identity;

/// <summary>Outcome of validating an access token: the principal, or why not.</summary>
public sealed record TokenValidation(AuthPrincipal? Principal, bool IsExpired)
{
    public static readonly TokenValidation Invalid = new(null, IsExpired: false);
    public static readonly TokenValidation Expired = new(null, IsExpired: true);
}

/// <summary>
/// Signs and validates the ONE remaining JWT family: an integration account's
/// access token. Claim layout is the adapter's business; the application deals
/// in <see cref="AuthPrincipal"/>.
///
/// <para>There used to be a second family — a refresh JWT with its own secret —
/// and it is gone. A refresh token is now an opaque value that matches a stored
/// digest (see <see cref="IntegrationTokenIssuer"/>), so there is nothing to
/// sign, nothing to verify, and no second key to keep.</para>
/// </summary>
public interface IIntegrationTokenCodec
{
    string Sign(AuthPrincipal principal, TimeSpan lifetime);

    /// <summary>Validates signature, and expiry unless ignored.</summary>
    Task<TokenValidation> ValidateAsync(string token, bool ignoreExpiration = false);
}
