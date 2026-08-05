using Hsm.Application.Abstractions;
using Hsm.Domain.Identity;

namespace Hsm.Application.Identity.Commands.IssueIntegrationTokens;

/// <summary>
/// Admin token re-issue for an EXISTING integration account: mints a fresh pair
/// through the same <see cref="IntegrationTokenIssuer"/> registering and
/// refreshing use, so the prior refresh token stops working. In-process admin
/// screen only; no REST route maps to it — over HTTP an integration renews its
/// own credential by refreshing, and an admin who needs to force a new one
/// revokes instead.
///
/// <para>The plaintext is returned exactly once; only its digest is persisted.
/// Admin-only: this hands out a live credential for an account that is not the
/// caller's own.</para>
/// </summary>
[RequireRole(Roles.Admin)]
public sealed record IssueIntegrationTokensCommand(Guid AccountId) : ICommand<IntegrationTokens>;
