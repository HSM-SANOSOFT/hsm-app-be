using Hsm.Application.Abstractions;
using Hsm.Application.Auth;

namespace Hsm.Application.Users.Queries.ListUsers;

public sealed class ListUsersHandler(IUserStore users) : IRequestHandler<ListUsersQuery, ListUsersResult>
{
    public async Task<ListUsersResult> HandleAsync(ListUsersQuery request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (rows, totalItems) = await users.ListAsync(request.Page, request.Limit, ct);
        return new ListUsersResult(rows, request.Page, request.Limit, totalItems);
    }
}
