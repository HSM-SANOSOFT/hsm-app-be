using Hsm.Application.Abstractions;
using Hsm.Domain.Identity;

namespace Hsm.Application.Users.Commands.ChangeUserRole;

/// <summary>
/// Admin-only role change: replaces the target's role
/// assignments via remove-then-add inside ONE transaction.
///
/// Role change vs. live sessions: authorization is decided from the CALLER'S
/// PRINCIPAL, so changing the row is only half the job — the sessions already
/// holding the old roles have to hear about it. The handler therefore bumps the
/// target's SECURITY STAMP, and the observable consequences are:
///   * a browser session for that account is REVOKED — the session cookie's
///     OnValidatePrincipal compares the stamp on the next request, finds the
///     mismatch and signs the holder out, so a demoted admin is not an admin on
///     their very next call (SessionRevocationTests);
///   * only THAT account's sessions are touched; the stamp is per user;
///   * an integration bearer token still keeps its roles for its whole
///     lifetime, because a JWT is not re-read from anywhere. Integrations hold
///     one role (integration) and no route grants on it, so nothing is
///     currently reachable that way — but revoking them is Task 14's to design.
/// </summary>
[RequireRole(Roles.Admin)]
public sealed record ChangeUserRoleCommand(Guid UserId, string Role) : ICommand<UserWithRoles>;
