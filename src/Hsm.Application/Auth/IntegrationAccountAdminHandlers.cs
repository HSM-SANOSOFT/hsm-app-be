using Hsm.Application.Errors;
using Hsm.Domain.Identity;

namespace Hsm.Application.Auth;

/// <summary>
/// Admin listing of integration accounts (plan U18). The frozen REST surface
/// has no such operation — this query exists for the in-process UI surface
/// only and adds no /v1 route. Like its endpoint-gated siblings
/// (SignupIntegrationHandler), it trusts the caller's admin gate.
/// </summary>
public sealed class ListIntegrationAccountsHandler(
    IIntegrationAccountStore accounts,
    IIntegrationRefreshTokenStore tokens)
{
    public sealed record Item(IntegrationAccount Account, bool HasActiveToken);

    public async Task<IReadOnlyList<Item>> HandleAsync(CancellationToken ct = default)
    {
        var rows = await accounts.ListAsync(ct);
        var items = new List<Item>(rows.Count);
        foreach (var account in rows)
        {
            var active = await tokens.FindActiveAsync(account.Id, ct);
            items.Add(new Item(account, active is not null));
        }

        return items;
    }
}

/// <summary>
/// Admin token issuance for an EXISTING integration account (plan U18):
/// mints a fresh pair and rotates the stored refresh hash through the same
/// <see cref="TokenIssuer"/> path login uses — the prior refresh token stops
/// working. In-process UI surface only; no /v1 route. The plaintext pair is
/// returned exactly once and persisted only as a bcrypt hash.
/// </summary>
public sealed class IssueIntegrationTokensHandler(IIntegrationAccountStore accounts, TokenIssuer issuer)
{
    public async Task<TokenPair> HandleAsync(Guid accountId, CancellationToken ct = default)
    {
        var account = await accounts.FindByIdAsync(accountId, ct)
            ?? throw ApiException.NotFound($"Integration account with id {accountId} not found");

        if (!account.IsActive)
        {
            throw ApiException.BadRequest("Integration account is inactive");
        }

        var principal = new AuthPrincipal
        {
            Id = account.Id.ToString(),
            Name = account.Name,
            Roles = [Roles.Integration],
        };
        return await issuer.IssueAsync(principal, ct);
    }
}

/// <summary>
/// Admin revocation of an integration account's active refresh token (plan
/// U18): deactivates the store row so the token cannot refresh again.
/// Idempotent — revoking an account with no active token is a no-op, unlike
/// the frozen logoutIntegration (which requires presenting the token itself
/// and rejects an already-logged-out account). In-process UI surface only.
/// </summary>
public sealed class RevokeIntegrationTokensHandler(IIntegrationRefreshTokenStore tokens)
{
    /// <summary>Returns the number of rows deactivated (0 when none were active).</summary>
    public Task<int> HandleAsync(Guid accountId, CancellationToken ct = default) =>
        tokens.DeactivateActiveAsync(accountId, ct);
}
