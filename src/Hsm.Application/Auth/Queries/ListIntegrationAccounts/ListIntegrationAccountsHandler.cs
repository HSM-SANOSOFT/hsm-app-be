using Hsm.Application.Abstractions;

namespace Hsm.Application.Auth.Queries.ListIntegrationAccounts;

public sealed class ListIntegrationAccountsHandler(
    IIntegrationAccountStore accounts,
    IIntegrationRefreshTokenStore tokens)
    : IRequestHandler<ListIntegrationAccountsQuery, IReadOnlyList<IntegrationAccountListItem>>
{
    public async Task<IReadOnlyList<IntegrationAccountListItem>> HandleAsync(
        ListIntegrationAccountsQuery request, CancellationToken ct)
    {
        var rows = await accounts.ListAsync(ct);
        var items = new List<IntegrationAccountListItem>(rows.Count);
        foreach (var account in rows)
        {
            var active = await tokens.FindActiveAsync(account.Id, ct);
            items.Add(new IntegrationAccountListItem(account, active is not null));
        }

        return items;
    }
}
