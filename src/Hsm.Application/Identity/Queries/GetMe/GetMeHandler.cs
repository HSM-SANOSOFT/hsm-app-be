using Hsm.Application.Abstractions;
using Hsm.Application.Errors;
using Hsm.Domain.Identity;
using Microsoft.AspNetCore.Identity;

namespace Hsm.Application.Identity.Queries.GetMe;

public sealed class GetMeHandler(UserManager<HsmUser> users, ICurrentPrincipal principal)
    : IRequestHandler<GetMeQuery, HsmUser>
{
    public async Task<HsmUser> HandleAsync(GetMeQuery request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        // AuthorizationBehavior has already refused an actor-less dispatch; the
        // throw keeps the same 401 if this ever runs outside the pipeline.
        var actor = principal.Actor ?? throw new UnauthorizedException();

        // 404 rather than 401 for a soft-deleted or vanished account: the
        // credential was valid, the subject is not there. An INTEGRATION
        // account lands here too — its subject is an integration row, not a
        // user row — and 404 is the honest answer for it as well: a machine
        // account has no profile to read.
        var user = await users.FindByIdAsync(actor.Id);
        return user is null || user.DeletedAt is not null
            ? throw new NotFoundException("User", actor.Id)
            : user;
    }
}
