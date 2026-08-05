namespace Hsm.Application.Identity;

/// <summary>
/// An integration account's issued credential: a JWT access token, an opaque
/// refresh token, and the id of the account both belong to.
///
/// <para>The two halves are deliberately different KINDS of thing. The access
/// token is a signed statement about who is calling, so it must be readable by
/// the bearer handler that validates it. The refresh token states nothing — it
/// is 256 bits of entropy whose only property is matching a stored digest — so
/// there is nothing in it to read, validate or leak. See
/// <see cref="IntegrationTokenIssuer"/>.</para>
///
/// <para><c>AccountId</c> rides along because the caller that provisions an
/// account needs it, and the alternative was worse: the shell used to recover
/// it by base64-decoding the access token's payload without validating it.</para>
/// </summary>
public sealed record IntegrationTokens(Guid AccountId, string AccessToken, string RefreshToken);

/// <summary>
/// The identity carried in an integration's access JWT: an id, the account
/// name, and the <c>integration</c> role.
///
/// <para>It is still shaped for a human as well — username, email, the
/// onboarding claim — because the shell builds one to publish a signed-in staff
/// member's identity onto a Blazor circuit
/// (<c>Hsm.Web.Auth.AuthPrincipalClaims</c>), and the API suites build one to
/// exercise the bearer handler. Nothing SIGNS a human one any more: people hold
/// an Identity session cookie.</para>
/// </summary>
public sealed record AuthPrincipal
{
    public required string Id { get; init; }
    public string? Username { get; init; }
    public string? Email { get; init; }
    public string? FirstName { get; init; }
    public string? FirstLastName { get; init; }

    /// <summary>Integration account name (integration principals only).</summary>
    public string? Name { get; init; }

    public required IReadOnlyList<string> Roles { get; init; }

    /// <summary>
    /// ISO-8601 completion timestamp or null while pending. Present (possibly
    /// null) for users; absent for integrations.
    /// </summary>
    public string? OnboardingCompletedAt { get; init; }

    /// <summary>True when the claim set carried onboardingCompletedAt (users do, integrations don't).</summary>
    public bool HasOnboardingClaim { get; init; }

    /// <summary>Unix seconds, present only on validated (incoming) tokens.</summary>
    public long? IssuedAt { get; init; }

    /// <summary>Unix seconds, present only on validated (incoming) tokens.</summary>
    public long? ExpiresAt { get; init; }

    public bool IsIntegration => Roles.Contains(Domain.Identity.Roles.Integration);
}
