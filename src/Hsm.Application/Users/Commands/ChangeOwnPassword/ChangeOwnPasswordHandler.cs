using Hsm.Application.Abstractions;
using Hsm.Application.Auth;
using Hsm.Application.Errors;

namespace Hsm.Application.Users.Commands.ChangeOwnPassword;

/// <summary>
/// The current password is bcrypt-verified BEFORE anything is written; a wrong
/// current password fails without touching the stored hash — and without
/// revoking refresh tokens or the active session (only the reset-token flow
/// revokes).
/// </summary>
public sealed class ChangeOwnPasswordHandler(
    IUserStore users,
    IPasswordHasher hasher,
    ICurrentPrincipal principal)
    : IRequestHandler<ChangeOwnPasswordCommand, Unit>
{
    public async Task<Unit> HandleAsync(ChangeOwnPasswordCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var actor = principal.Actor ?? throw ApiException.Unauthorized();
        var userId = Guid.Parse(actor.Id);

        var user = await users.FindByIdAsync(userId, ct)
            ?? throw ApiException.NotFound($"User with id {userId} not found");

        if (!hasher.Verify(request.CurrentPassword, user.PasswordHash))
        {
            throw ApiException.Unauthorized("Current password is incorrect");
        }

        await users.UpdatePasswordAsync(userId, hasher.Hash(request.NewPassword), ct);
        return Unit.Value;
    }
}
