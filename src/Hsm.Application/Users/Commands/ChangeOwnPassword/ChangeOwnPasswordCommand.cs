using Hsm.Application.Abstractions;
using Hsm.Domain.Identity;

namespace Hsm.Application.Users.Commands.ChangeOwnPassword;

/// <summary>
/// Self-service password change (frozen changeOwnPassword). Like
/// <c>UpdateOwnProfileCommand</c> it carries no user id — the target is the
/// actor from <see cref="ICurrentPrincipal"/>.
///
/// <para>It returns the UPDATED USER rather than <c>Unit</c> for the same
/// reason <c>LoginCommand</c> returns one: changing a password rotates the
/// security stamp, which revokes every session for the account — including the
/// caller's own — and reissuing the caller's cookie is transport, so only the
/// door that has an HTTP response can do it. The command does the application
/// work and hands back what the door needs to finish the job.</para>
/// </summary>
public sealed record ChangeOwnPasswordCommand(string CurrentPassword, string NewPassword)
    : ICommand<HsmUser>;
