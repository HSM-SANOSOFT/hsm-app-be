using Hsm.Application.Abstractions;

namespace Hsm.Application.Auth.Commands.RecoverUsername;

public sealed class RecoverUsernameHandler(IUserDirectory users, IRecoveryEmailer emailer)
    : IRequestHandler<RecoverUsernameCommand, Unit>
{
    public async Task<Unit> HandleAsync(RecoverUsernameCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await users.FindLiveByEmailAsync(request.Email, ct);
        if (user is null || !user.IsActive)
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
