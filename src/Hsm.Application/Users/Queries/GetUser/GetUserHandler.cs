using Hsm.Application.Abstractions;
using Hsm.Application.Auth;
using Hsm.Application.Errors;
using Hsm.Domain.Identity;

namespace Hsm.Application.Users.Queries.GetUser;

public sealed class GetUserHandler(IUserStore users) : IRequestHandler<GetUserQuery, User>
{
    public async Task<User> HandleAsync(GetUserQuery request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await users.FindByIdAsync(request.UserId, ct)
            ?? throw ApiException.NotFound($"User with id {request.UserId} not found");
    }
}
