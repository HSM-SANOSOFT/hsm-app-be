using Hsm.Application.Abstractions;
using Hsm.Application.Errors;
using Hsm.Domain.Identity;
using Microsoft.AspNetCore.Identity;

namespace Hsm.Application.Users.Queries.GetUser;

public sealed class GetUserHandler(UserManager<HsmUser> users) : IRequestHandler<GetUserQuery, UserWithRoles>
{
    public async Task<UserWithRoles> HandleAsync(GetUserQuery request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await users.FindByIdAsync(request.UserId.ToString());
        if (user is null || user.DeletedAt is not null)
        {
            throw new NotFoundException("User", request.UserId);
        }

        return new UserWithRoles(user, [.. await users.GetRolesAsync(user)]);
    }
}
