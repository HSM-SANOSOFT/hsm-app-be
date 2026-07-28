using Hsm.Application.Auth;
using Hsm.Contracts.Ui;

namespace Hsm.Web.Services;

/// <summary>
/// Host-side integration account administration (plan U18, screen 3): admin
/// gate first, then the in-process handlers. Token plaintext flows through
/// the returned DTO exactly once — this service holds no token state, and
/// the stores persist only bcrypt hashes, so nothing on this path can
/// re-produce a secret after issuance.
/// </summary>
public sealed class IntegrationAccountsUiService(
    UiServiceGate gate,
    ListIntegrationAccountsHandler list,
    SignupIntegrationHandler provision,
    IssueIntegrationTokensHandler issue,
    RevokeIntegrationTokensHandler revoke) : IIntegrationAccountsUiService
{
    public async Task<IReadOnlyList<IntegrationAccountDto>> ListAccountsAsync(
        CancellationToken cancellationToken = default)
    {
        await gate.RequireAdminAsync();
        var items = await list.HandleAsync(cancellationToken);
        return [.. items.Select(item => new IntegrationAccountDto(
            item.Account.Id.ToString(),
            item.Account.Name,
            item.Account.Description,
            item.Account.Functionality,
            item.Account.IsActive,
            item.HasActiveToken))];
    }

    public async Task<IssuedIntegrationTokensDto> ProvisionAsync(
        NewIntegrationAccountDto command, CancellationToken cancellationToken = default)
    {
        await gate.RequireAdminAsync();
        if (!UiIntegrationFunctionality.All.Contains(command.Functionality, StringComparer.Ordinal))
        {
            // The endpoint's oneOf validation equivalent.
            throw new ArgumentException(
                $"Funcionalidad desconocida: '{command.Functionality}'.", nameof(command));
        }

        var tokens = await provision.HandleAsync(
            new SignupIntegrationHandler.Command(command.Name, command.Description, command.Functionality),
            cancellationToken);
        return new IssuedIntegrationTokensDto(
            AccountId: DecodeAccountId(tokens.AccessToken),
            AccountName: command.Name,
            AccessToken: tokens.AccessToken,
            RefreshToken: tokens.RefreshToken);
    }

    public async Task<IssuedIntegrationTokensDto> IssueTokensAsync(
        string accountId, CancellationToken cancellationToken = default)
    {
        await gate.RequireAdminAsync();
        var id = Guid.Parse(accountId);
        var tokens = await issue.HandleAsync(id, cancellationToken);
        var account = (await list.HandleAsync(cancellationToken))
            .FirstOrDefault(item => item.Account.Id == id);
        return new IssuedIntegrationTokensDto(
            accountId, account?.Account.Name ?? accountId, tokens.AccessToken, tokens.RefreshToken);
    }

    public async Task RevokeTokensAsync(string accountId, CancellationToken cancellationToken = default)
    {
        await gate.RequireAdminAsync();
        await revoke.HandleAsync(Guid.Parse(accountId), cancellationToken);
    }

    /// <summary>
    /// Reads the frozen id claim from a just-minted JWT (no validation — this
    /// host signed it a moment ago). The provisioning handler returns only
    /// the token pair, so the created account's id rides in the token.
    /// </summary>
    private static string DecodeAccountId(string jwt)
    {
        var segment = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
        segment += (segment.Length % 4) switch { 2 => "==", 3 => "=", _ => string.Empty };
        using var doc = System.Text.Json.JsonDocument.Parse(Convert.FromBase64String(segment));
        return doc.RootElement.TryGetProperty("id", out var id)
            ? id.GetString() ?? string.Empty
            : string.Empty;
    }
}
