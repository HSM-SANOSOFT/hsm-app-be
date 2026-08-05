using FluentValidation;
using Hsm.Application.Abstractions;
using Hsm.Application.Identity;
using Hsm.Application.Errors;
using Hsm.Domain.Identity;
using Microsoft.AspNetCore.Identity;

namespace Hsm.Application.Users.Commands.ChangeOwnPassword;

/// <summary>
/// The current password is verified BEFORE anything is written; a wrong
/// current password fails without touching the stored hash and without
/// disturbing any session.
///
/// <para>A SUCCESSFUL change does revoke sessions: UserManager rotates the
/// security stamp, and the cookie validator refuses every cookie carrying the
/// old one on its next request. That is the point — a password change is how
/// you evict someone who has your old one. The caller's OWN session is the
/// exception, and the endpoint repairs it by reissuing that one cookie from
/// the user this returns.</para>
/// </summary>
public sealed class ChangeOwnPasswordHandler(
    UserManager<HsmUser> users,
    ICurrentPrincipal principal)
    : IRequestHandler<ChangeOwnPasswordCommand, HsmUser>
{
    public async Task<HsmUser> HandleAsync(ChangeOwnPasswordCommand request, CancellationToken ct)
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

        // Carries the ROTATED security stamp, which the endpoint needs to mint
        // the caller a replacement cookie.
        return user;
    }
}
