using Hsm.Application.Abstractions;

namespace Hsm.Application.Users.Commands.UpdateOwnProfile;

/// <summary>
/// Self-service profile update: ONLY firstName and
/// email are reachable through this path — the role and every other column
/// cannot be changed by the profile owner (R6; self-escalation is impossible
/// because the request type has no other field to carry one).
///
/// The command carries NO user id: the target is always the actor, taken from
/// <see cref="ICurrentPrincipal"/>. "Can I edit someone else by passing their
/// id" is not a question this shape can ask.
/// </summary>
public sealed record UpdateOwnProfileCommand(string? FirstName, string? Email) : ICommand<UserWithRoles>;
