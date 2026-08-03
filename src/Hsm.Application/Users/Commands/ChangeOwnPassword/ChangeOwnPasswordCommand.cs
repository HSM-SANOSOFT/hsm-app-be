using Hsm.Application.Abstractions;

namespace Hsm.Application.Users.Commands.ChangeOwnPassword;

/// <summary>
/// Self-service password change (frozen changeOwnPassword). Like
/// <c>UpdateOwnProfileCommand</c> it carries no user id — the target is the
/// actor from <see cref="ICurrentPrincipal"/>.
/// </summary>
public sealed record ChangeOwnPasswordCommand(string CurrentPassword, string NewPassword) : ICommand<Unit>;
