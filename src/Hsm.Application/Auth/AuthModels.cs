namespace Hsm.Application.Auth;

/// <summary>An access/refresh token pair, the frozen ITokens shape.</summary>
public sealed record TokenPair(string AccessToken, string RefreshToken);

/// <summary>
/// The identity carried in a JWT: a human user (username/email/... set) or an
/// integration account (Name set, Roles contains "integration"). Field names
/// mirror the frozen JWT claims exactly — they are contract.
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

/// <summary>Which of the two signing secrets a token belongs to.</summary>
public enum TokenKind
{
    Access,
    Refresh,
}

/// <summary>
/// Frozen token lifetimes (auth.service.ts generateTokens): browser users get
/// 15m access / 1d refresh; integrations get 1d access / 30d refresh.
/// </summary>
public static class TokenLifetimes
{
    public static readonly TimeSpan UserAccess = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan UserRefresh = TimeSpan.FromDays(1);
    public static readonly TimeSpan IntegrationAccess = TimeSpan.FromDays(1);
    public static readonly TimeSpan IntegrationRefresh = TimeSpan.FromDays(30);
}
