using Hsm.Application.Abstractions;
using Hsm.Domain.Identity;

namespace Hsm.Application.Auth.Commands.LogoutIntegration;

/// <summary>
/// Integration sign-out by presented token (frozen logoutIntegration).
/// Authenticated with NO pending-onboarding exemption: unlike
/// <c>LogoutCommand</c> this is an administration call about someone else's
/// machine account, not a self-service escape hatch, so a pending caller has
/// no business reaching it.
///
/// <see cref="RequireRoleAttribute"/> was added in Task 15. The frozen route
/// required admin at the edge (@Roles(admin) on the controller method) while
/// this request type carried only "authenticated"; the difference was
/// invisible while the edge still ran the role check, and would have become a
/// real privilege hole the moment it was deleted — any authenticated user
/// could have signed out any machine account whose token they held.
/// </summary>
[RequireRole(Roles.Admin)]
public sealed record LogoutIntegrationCommand(string Token) : ICommand<Unit>;
