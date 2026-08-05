using Hsm.Application.Abstractions;
using Hsm.Domain.Identity;

namespace Hsm.Application.Identity.Commands.LogoutIntegration;

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
/// <para><see cref="RequireRoleAttribute"/> is on the REQUEST, not the route,
/// and it has to be: without it, any authenticated user could sign out any
/// machine account whose token they held.</para>
/// </summary>
[RequireRole(Roles.Admin)]
public sealed record LogoutIntegrationCommand(string Token) : ICommand<Unit>;
