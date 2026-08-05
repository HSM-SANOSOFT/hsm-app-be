using Hsm.Application.Abstractions;
using Hsm.Domain.Identity;

namespace Hsm.Application.Identity.Queries.ListIntegrationAccounts;

/// <summary>
/// Admin listing of integration accounts (plan U18). This query exists for
/// the in-process UI surface only and adds no /v1 route. Before the pipeline
/// it "trusted the caller's admin gate"; now it carries the gate itself.
/// </summary>
[RequireRole(Roles.Admin)]
public sealed record ListIntegrationAccountsQuery : IQuery<IReadOnlyList<IntegrationAccountListItem>>;

/// <summary>One machine account and whether it currently has a usable token.</summary>
public sealed record IntegrationAccountListItem(IntegrationAccount Account, bool HasActiveToken);
