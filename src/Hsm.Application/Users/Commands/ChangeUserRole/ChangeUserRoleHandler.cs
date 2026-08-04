using Hsm.Application.Abstractions;
using Hsm.Application.Auth;
using Hsm.Application.Errors;
using Hsm.Domain.Identity;
using Microsoft.AspNetCore.Identity;

namespace Hsm.Application.Users.Commands.ChangeUserRole;

public sealed class ChangeUserRoleHandler(UserManager<HsmUser> users)
    : IRequestHandler<ChangeUserRoleCommand, UserWithRoles>
{
    public async Task<UserWithRoles> HandleAsync(ChangeUserRoleCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await users.FindByIdAsync(request.UserId.ToString());
        if (user is null || user.DeletedAt is not null)
        {
            throw new NotFoundException("User", request.UserId);
        }

        // Remove FIRST, then add — the frozen statement order, so re-assigning
        // a role the user already holds cannot trip the (user, role) primary
        // key. Both halves run through UserManager's EF store, which shares
        // this scope's DbContext, so both land inside the single transaction
        // TransactionBehavior opened around this command: an exception between
        // them rolls the removal back rather than leaving the account with no
        // roles at all (proved by RoleReplacementTransactionTests).
        var held = await users.GetRolesAsync(user);
        if (held.Count > 0)
        {
            (await users.RemoveFromRolesAsync(user, held)).ThrowIfFailed("role");
        }

        (await users.AddToRoleAsync(user, request.Role)).ThrowIfFailed("role");
        return new UserWithRoles(user, [request.Role]);
    }
}
