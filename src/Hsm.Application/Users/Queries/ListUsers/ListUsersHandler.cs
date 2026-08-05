using Hsm.Application.Abstractions;
using Hsm.Application.Identity;
using Hsm.Contracts;

namespace Hsm.Application.Users.Queries.ListUsers;

public sealed class ListUsersHandler(IUserDirectory users)
    : IRequestHandler<ListUsersQuery, PagedResult<UserWithRoles>>
{
    public async Task<PagedResult<UserWithRoles>> HandleAsync(ListUsersQuery request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var page = await users.ListAsync(request.Page, request.PageSize, ct);

        // One extra round trip for the whole page, not one per row: the reason
        // IUserDirectory.RolesForAsync exists at all.
        var roles = await users.RolesForAsync([.. page.Items.Select(u => u.Id)], ct);
        return page.Map(user => new UserWithRoles(
            user, roles.TryGetValue(user.Id, out var held) ? held : []));
    }
}
