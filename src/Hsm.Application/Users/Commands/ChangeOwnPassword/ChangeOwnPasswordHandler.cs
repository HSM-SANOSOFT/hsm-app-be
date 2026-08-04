using FluentValidation;
using Hsm.Application.Abstractions;
using Hsm.Application.Auth;
using Hsm.Application.Errors;
using Hsm.Domain.Identity;
using Microsoft.AspNetCore.Identity;

namespace Hsm.Application.Users.Commands.ChangeOwnPassword;

/// <summary>
/// The current password is verified BEFORE anything is written; a wrong
/// current password fails without touching the stored hash — and without
/// revoking refresh tokens or the active session (only the reset-token flow
/// revokes).
/// </summary>
public sealed class ChangeOwnPasswordHandler(
    UserManager<HsmUser> users,
    ICurrentPrincipal principal)
    : IRequestHandler<ChangeOwnPasswordCommand, Unit>
{
    public async Task<Unit> HandleAsync(ChangeOwnPasswordCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var actor = principal.Actor ?? throw new UnauthorizedException();

        var user = await users.FindByIdAsync(actor.Id);
        if (user is null || user.DeletedAt is not null)
        {
            throw new NotFoundException("User", actor.Id);
        }

        var result = await users.ChangePasswordAsync(
            user, request.CurrentPassword, request.NewPassword);
        if (result.HasCode("PasswordMismatch"))
        {
            // The caller's session is fine; a 401 here reads as "you were
            // signed out" and causes spurious re-auth loops. Reported on the
            // field that was actually wrong rather than through
            // ThrowIfFailed's single-field mapping.
            throw new ValidationException(
                [new FluentValidation.Results.ValidationFailure("currentPassword", "Current password is incorrect.")]);
        }

        // Everything else Identity can report here is about the NEW password.
        result.ThrowIfFailed("newPassword");
        return Unit.Value;
    }
}
