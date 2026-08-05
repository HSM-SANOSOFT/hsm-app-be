namespace Hsm.Contracts.Ui;

/// <summary>
/// Integration accounts and tokens (plan U18, screen 3) — the screen with no
/// alternative home. Provisioning and token issuance return the token pair
/// EXACTLY ONCE: no method on this surface can retrieve a secret after
/// issuance (the stores only hold bcrypt hashes), so a secret lost is a
/// secret rotated, never recovered.
/// </summary>
public interface IIntegrationAccountsUiService
{
    Task<IReadOnlyList<IntegrationAccountDto>> ListAccountsAsync(CancellationToken cancellationToken = default);

    /// <summary>Provisions an account and issues its first token pair.</summary>
    Task<IssuedIntegrationTokensDto> ProvisionAsync(
        NewIntegrationAccountDto command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Issues a fresh token pair for an existing account, rotating the
    /// stored refresh hash — the prior refresh token stops working.
    /// </summary>
    Task<IssuedIntegrationTokensDto> IssueTokensAsync(string accountId, CancellationToken cancellationToken = default);

    /// <summary>Deactivates the account's active refresh token, if any.</summary>
    Task RevokeTokensAsync(string accountId, CancellationToken cancellationToken = default);
}

/// <summary>One integration account as the screen renders it.</summary>
public sealed record IntegrationAccountDto(
    string Id,
    string Name,
    string Description,
    string Functionality,
    bool IsActive,
    bool HasActiveToken);

/// <summary>What provisioning needs.</summary>
public sealed record NewIntegrationAccountDto(string Name, string Description, string Functionality);

/// <summary>
/// An issued token pair. This DTO is the ONLY carrier of integration secrets
/// across the UI boundary; it exists in the issuing view's state and nowhere
/// else.
/// </summary>
public sealed record IssuedIntegrationTokensDto(
    string AccountId,
    string AccountName,
    string AccessToken,
    string RefreshToken);

/// <summary>
/// Role functionality values, re-declared on this side of the boundary (the
/// component library cannot reference the domain catalog; the string values
/// are contract).
/// </summary>
public static class UiIntegrationFunctionality
{
    public const string Prod = "prod";
    public const string Staging = "staging";
    public const string Dev = "dev";

    public static readonly IReadOnlyList<string> All = [Prod, Staging, Dev];
}
