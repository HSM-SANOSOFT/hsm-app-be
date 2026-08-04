using Hsm.Application.Abstractions;
using Hsm.Domain.Identity;

namespace Hsm.Application.Users.Commands.ChangeUserRole;

/// <summary>
/// Admin-only role change (frozen changeUserRole): replaces the target's role
/// assignments via remove-then-add inside ONE transaction.
///
/// Role change vs. live sessions: authorization is decided from the CALLER'S
/// PRINCIPAL, and this command changes the database row without touching any
/// session. The row is authoritative immediately; a session already holding
/// the old roles is not. Observable consequences:
///   * a browser session keeps authorizing with its OLD roles until the
///     Identity cookie's principal is refreshed, which the security-stamp
///     validator does on its validation interval (30 minutes by default) by
///     re-reading the user and its roles;
///   * an integration bearer token keeps its OLD roles for its whole lifetime,
///     because a JWT is not re-read from anywhere;
///   * either way the NEW roles take effect on the next fresh sign-in, where
///     roles are read from the database.
/// Making the change instantaneous would mean bumping the target's security
/// stamp (browsers) and revoking its tokens (integrations) — a deliberate,
/// separate decision, not something to slip into a role edit.
/// </summary>
[RequireRole(Roles.Admin)]
public sealed record ChangeUserRoleCommand(Guid UserId, string Role) : ICommand<UserWithRoles>;
