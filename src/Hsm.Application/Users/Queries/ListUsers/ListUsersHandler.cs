using Hsm.Application.Abstractions;
using Hsm.Application.Auth;
using Hsm.Contracts;
using Hsm.Domain.Identity;

namespace Hsm.Application.Users.Queries.ListUsers;

public sealed class ListUsersHandler(IUserStore users) : IRequestHandler<ListUsersQuery, PagedResult<User>>
{
    public Task<PagedResult<User>> HandleAsync(ListUsersQuery request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        return users.ListAsync(request.Page, request.PageSize, ct);
    }
}
