using Hsm.Application.Abstractions;
using Hsm.Domain.Identity;

namespace Hsm.Application.Auth.Commands.RevokeIntegrationTokens;

/// <summary>
/// Admin revocation of an integration account's active refresh token (plan
/// U18): deactivates the store row so the token cannot refresh again.
/// Idempotent — revoking an account with no active token is a no-op, unlike
/// <c>LogoutIntegrationCommand</c> (which requires presenting the token itself
/// and rejects an already-logged-out account). That idempotence is behavior,
/// not a missing guard: the admin screen's "revoke" button must not fail
/// because someone else revoked first. In-process UI surface only.
/// </summary>
/// <remarks>Result is the number of rows deactivated (0 when none were active).</remarks>
[RequireRole(Roles.Admin)]
public sealed record RevokeIntegrationTokensCommand(Guid AccountId) : ICommand<int>;
