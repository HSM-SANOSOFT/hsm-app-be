using Hsm.Application.Abstractions;
using Hsm.Domain.Identity;

namespace Hsm.Application.Auth.Commands.LogoutIntegration;

/// <summary>
/// Integration sign-out by presented token: revokes whatever credential the
/// named account currently holds. EITHER half of the pair identifies it — the
/// access token names the account in a claim, the opaque refresh token matches
/// a stored digest — so an operator can revoke with whichever one they were
/// given.
///
/// <para>No pending-onboarding exemption: unlike a person signing themselves
/// out, this is an administration call about someone else's machine account,
/// so a caller who has not finished onboarding has no business reaching it.</para>
///
/// <para><see cref="RequireRoleAttribute"/> is on the REQUEST, not the route.
/// The frozen system required admin at the edge while this type carried only
/// "authenticated"; the difference was invisible while the edge still ran the
/// check, and would have become a real privilege hole the moment it was deleted
/// — any authenticated user could have signed out any machine account whose
/// token they held.</para>
/// </summary>
[RequireRole(Roles.Admin)]
public sealed record LogoutIntegrationCommand(string Token) : ICommand<Unit>;
