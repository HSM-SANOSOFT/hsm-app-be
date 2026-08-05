using Hsm.Application.Abstractions;
using Hsm.Application.Identity;
using Hsm.Application.Errors;
using Hsm.Domain.Identity;
using Microsoft.AspNetCore.Identity;

namespace Hsm.Application.Users.Commands.UpdateOwnProfile;

public sealed class UpdateOwnProfileHandler(
    UserManager<HsmUser> users,
    ICurrentPrincipal principal)
    : IRequestHandler<UpdateOwnProfileCommand, UserWithRoles>
{
    public async Task<UserWithRoles> HandleAsync(UpdateOwnProfileCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        // AuthorizationBehavior has already refused an actor-less dispatch;
        // the throw keeps the same 401 if this ever runs outside the pipeline.
        var actor = principal.Actor ?? throw new UnauthorizedException();

        var user = await users.FindByIdAsync(actor.Id);
        if (user is null || user.DeletedAt is not null)
        {
            throw new NotFoundException("User", actor.Id);
        }

        var changed = false;
        if (request.FirstName is not null)
        {
            user.FirstName = request.FirstName;
            changed = true;
        }

        if (request.Email is not null)
        {
            // Through UserManager, not the entity: it re-derives
            // NormalizedEmail, which every lookup and the unique index use.
            user.Email = request.Email;
            changed = true;
        }

        if (changed)
        {
            user.UpdatedAt = DateTimeOffset.UtcNow;
            (await users.UpdateAsync(user)).ThrowIfFailed("email");
        }

        return new UserWithRoles(user, [.. await users.GetRolesAsync(user)]);
    }
}
