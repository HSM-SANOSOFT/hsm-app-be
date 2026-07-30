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

        // The endpoint's isIn validation makes this unreachable in practice;
        // kept for parity with the frozen service-level guard.
        if (RoleCatalog.DomainOf(request.Role) is null)
        {
            throw ApiException.BadRequest($"Unknown role '{request.Role}'");
        }

        var user = await users.FindByIdAsync(request.UserId, ct)
            ?? throw ApiException.NotFound($"User with id {request.UserId} not found");

        var replaced = await users.ReplaceRolesAsync(request.UserId, [request.Role], ct);
        await unitOfWork.SaveChangesAsync(ct);

        // Return the user with the freshly persisted role rows — no re-read.
        user.Roles = [.. replaced];
        return user;
    }
}
