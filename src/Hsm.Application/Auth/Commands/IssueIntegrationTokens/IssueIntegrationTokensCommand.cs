using Hsm.Application.Abstractions;
using Hsm.Domain.Identity;

namespace Hsm.Application.Auth.Commands.IssueIntegrationTokens;

/// <summary>
/// Admin token issuance for an EXISTING integration account (plan U18):
/// mints a fresh pair and rotates the stored refresh hash through the same
/// <see cref="TokenIssuer"/> path login uses — the prior refresh token stops
/// working. In-process UI surface only; no /v1 route. The plaintext pair is
/// returned exactly once and persisted only as a bcrypt hash.
///
/// Admin-only: this hands out a live credential for an account that is not the
/// caller's own.
/// </summary>
[RequireRole(Roles.Admin)]
public sealed record IssueIntegrationTokensCommand(Guid AccountId) : ICommand<TokenPair>;
