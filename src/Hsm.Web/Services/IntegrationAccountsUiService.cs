using Hsm.Application.Abstractions;
using Hsm.Application.Auth.Commands.IssueIntegrationTokens;
using Hsm.Application.Auth.Commands.RevokeIntegrationTokens;
using Hsm.Application.Auth.Commands.SignupIntegration;
using Hsm.Application.Auth.Queries.ListIntegrationAccounts;
using Hsm.Contracts.Ui;
using Hsm.Web.Auth;

namespace Hsm.Web.Services;

/// <summary>
/// Host-side integration account administration (plan U18, screen 3): the
/// actor first, then the pipeline. Token plaintext flows through the returned
/// DTO exactly once — this service holds no token state, and the stores
/// persist only bcrypt hashes, so nothing on this path can re-produce a secret
/// after issuance.
///
/// <see cref="ShellActor"/> runs first in every method for one reason only:
/// to publish the circuit's identity so the pipeline's
/// [RequireRole(Roles.Admin)] has something to authorize. It decides nothing.
/// </summary>
public sealed class IntegrationAccountsUiService(
    ShellActor shellActor,
    IDispatcher dispatcher) : IIntegrationAccountsUiService
{
    public async Task<IReadOnlyList<IntegrationAccountDto>> ListAccountsAsync(
        CancellationToken cancellationToken = default)
    {
        await shellActor.InstallAsync(cancellationToken);
        var items = await dispatcher.Send(new ListIntegrationAccountsQuery(), cancellationToken);
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
        await shellActor.InstallAsync(cancellationToken);
        if (!UiIntegrationFunctionality.All.Contains(command.Functionality, StringComparer.Ordinal))
        {
            // The endpoint's oneOf validation equivalent.
            throw new ArgumentException(
                $"Funcionalidad desconocida: '{command.Functionality}'.", nameof(command));
        }

        var tokens = await dispatcher.Send(
            new SignupIntegrationCommand(command.Name, command.Description, command.Functionality),
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
        await shellActor.InstallAsync(cancellationToken);
        var id = Guid.Parse(accountId);
        var tokens = await dispatcher.Send(new IssueIntegrationTokensCommand(id), cancellationToken);
        var account = (await dispatcher.Send(new ListIntegrationAccountsQuery(), cancellationToken))
            .FirstOrDefault(item => item.Account.Id == id);
        return new IssuedIntegrationTokensDto(
            accountId, account?.Account.Name ?? accountId, tokens.AccessToken, tokens.RefreshToken);
    }

    public async Task RevokeTokensAsync(string accountId, CancellationToken cancellationToken = default)
    {
        await shellActor.InstallAsync(cancellationToken);
        await dispatcher.Send(new RevokeIntegrationTokensCommand(Guid.Parse(accountId)), cancellationToken);
    }

    /// <summary>
    /// Reads the frozen id claim from a just-minted JWT (no validation — this
    /// host signed it a moment ago). The provisioning command returns only
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
