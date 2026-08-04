using Hsm.Domain.Identity;

namespace Hsm.Application.Users;

/// <summary>
/// A user together with the roles it holds.
///
/// <para><see cref="HsmUser"/> deliberately has no <c>Roles</c> navigation:
/// Identity keeps role assignments in their own join table and reaches them
/// through <c>UserManager</c>, so an eager collection on the entity would be a
/// second, drifting source of truth. But every caller that renders a user
/// renders its roles, and the projection has to carry both. This is that pair —
/// an application-layer result shape, not an entity.</para>
/// </summary>
public sealed record UserWithRoles(HsmUser User, IReadOnlyList<string> Roles);
