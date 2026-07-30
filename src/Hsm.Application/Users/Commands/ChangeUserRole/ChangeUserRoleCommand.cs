using Hsm.Application.Abstractions;
using Hsm.Domain.Identity;

namespace Hsm.Application.Users.Commands.ChangeUserRole;

/// <summary>
/// Admin-only role change (frozen changeUserRole): replaces the target's role
/// rows via delete-then-insert inside ONE transaction.
///
/// Role change vs. live sessions — PINNED FROZEN BEHAVIOR: authorization is
/// decided from the JWT's own roles claim (frozen RolesGuard), and neither
/// this command nor the frozen implementation revokes refresh tokens or any
/// server-side cache (none exists — the database row is authoritative
/// immediately). Observable consequences, each pinned by a contract test:
///   * an outstanding access token keeps authorizing with its OLD roles until
///     it expires (user tokens live ≤15 minutes);
///   * /v1/auth/refresh reissues from the refresh token's claims, so even a
///     refreshed pair still carries the OLD roles;
///   * the NEW roles take effect on the next fresh login, where credentials
///     and roles are re-read from the database.
/// </summary>
[RequireRole(Roles.Admin)]
public sealed record ChangeUserRoleCommand(Guid UserId, string Role) : ICommand<User>;
