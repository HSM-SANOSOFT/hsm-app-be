using FluentValidation;
using Hsm.Application.Abstractions;
using Hsm.Application.Auth;
using Hsm.Application.Errors;
using Hsm.Domain.Identity;

namespace Hsm.Application.Users.Commands.ChangeUserRole;

public sealed class ChangeUserRoleHandler(IUserStore users, IAuthUnitOfWork unitOfWork)
    : IRequestHandler<ChangeUserRoleCommand, User>
{
    public async Task<User> HandleAsync(ChangeUserRoleCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        // TEMPORARY inline guard: an unknown role is a request-shape problem
        // and belongs in a validator — Task 3 replaces this block with
        // ChangeUserRoleValidator. Kept here in the meantime because Task 3's
        // validator infrastructure does not exist yet; the HTTP door restricts
        // the role param to RoleCatalog.All, but in-process callers (e.g.
        // UsersAdminUiService) dispatch this command with no such check.
        if (RoleCatalog.DomainOf(request.Role) is null)
        {
            throw new ValidationException(
                [new FluentValidation.Results.ValidationFailure("role", $"Unknown role '{request.Role}'.")]);
        }

        var user = await users.FindByIdAsync(request.UserId, ct)
            ?? throw new NotFoundException("User", request.UserId);

        var replaced = await users.ReplaceRolesAsync(request.UserId, [request.Role], ct);
        await unitOfWork.SaveChangesAsync(ct);

        // Return the user with the freshly persisted role rows — no re-read.
        user.Roles = [.. replaced];
        return user;
    }
}
