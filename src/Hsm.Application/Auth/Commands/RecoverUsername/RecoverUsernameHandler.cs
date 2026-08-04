using Hsm.Application.Abstractions;
using Hsm.Domain.Identity;
using Microsoft.AspNetCore.Identity;

namespace Hsm.Application.Auth.Commands.RecoverUsername;

public sealed class RecoverUsernameHandler(UserManager<HsmUser> users, IRecoveryEmailer emailer)
    : IRequestHandler<RecoverUsernameCommand, Unit>
{
    public async Task<Unit> HandleAsync(RecoverUsernameCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await users.FindByEmailAsync(request.Email);
        if (user is null || !user.IsActive || user.DeletedAt is not null)
        {
            return Unit.Value; // Non-enumerating: nothing observable.
        }

        try
        {
            await emailer.SendUsernameReminderAsync(request.Email, user.UserName!, ct);
        }
        catch (Exception)
        {
            // Swallow: a known account must not 500 while an unknown one
            // returns the generic outcome.
        }

        return Unit.Value;
    }
}
