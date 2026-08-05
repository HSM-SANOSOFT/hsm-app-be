using Hsm.Application.Abstractions;
using Hsm.Application.Identity;
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

        // Remove FIRST, then add — so re-assigning a role the user already
        // holds cannot trip the (user, role) primary key. Both halves run
        // through UserManager's EF store, which shares
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

        // A role change is a REVOCATION, and a revocation that a live session
        // does not hear about is not one. Bumping the security stamp is how
        // Identity says "every session for this account is now stale": the
        // cookie's OnValidatePrincipal compares the stamp on the next request,
        // finds the mismatch, and signs the holder out — so a demoted admin
        // stops being an admin on their next request rather than whenever
        // their session happens to expire. Inside the same transaction as the
        // role rows, so a rollback cannot leave the stamp bumped for a role
        // change that never happened.
        (await users.UpdateSecurityStampAsync(user)).ThrowIfFailed("role");
        return new UserWithRoles(user, [request.Role]);
    }
}
