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

        var user = await users.FindByIdAsync(request.UserId, ct)
            ?? throw new NotFoundException("User", request.UserId);

        var replaced = await users.ReplaceRolesAsync(request.UserId, [request.Role], ct);
        await unitOfWork.SaveChangesAsync(ct);

        // Return the user with the freshly persisted role rows — no re-read.
        user.Roles = [.. replaced];
        return user;
    }
}
