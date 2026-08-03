using Hsm.Application.Abstractions;
using Hsm.Application.Auth;
using Hsm.Application.Errors;
using Hsm.Domain.Identity;

namespace Hsm.Application.Users.Commands.UpdateOwnProfile;

public sealed class UpdateOwnProfileHandler(
    IUserStore users,
    IAuthUnitOfWork unitOfWork,
    ICurrentPrincipal principal)
    : IRequestHandler<UpdateOwnProfileCommand, User>
{
    public async Task<User> HandleAsync(UpdateOwnProfileCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        // AuthorizationBehavior has already refused an actor-less dispatch;
        // the throw keeps the same 401 if this ever runs outside the pipeline.
        var actor = principal.Actor ?? throw new UnauthorizedException();
        var userId = Guid.Parse(actor.Id);

        var user = await users.FindByIdAsync(userId, ct)
            ?? throw new NotFoundException("User", actor.Id);

        var changed = false;
        if (request.FirstName is not null)
        {
            user.FirstName = request.FirstName;
            changed = true;
        }

        if (request.Email is not null)
        {
            user.Email = request.Email;
            changed = true;
        }

        if (changed)
        {
            user.UpdatedAt = DateTimeOffset.UtcNow;
            await unitOfWork.SaveChangesAsync(ct);
        }

        return user;
    }
}
