using System.Globalization;
using Hsm.Application.Abstractions;
using Hsm.Application.Errors;
using Hsm.Domain.Identity;
using Microsoft.AspNetCore.Identity;

namespace Hsm.Application.Auth.Commands.ForgotPassword;

public sealed class ForgotPasswordHandler(
    UserManager<HsmUser> users,
    IUserDirectory directory,
    IRecoveryEmailer emailer)
    : IRequestHandler<ForgotPasswordCommand, Unit>
{
    public async Task<Unit> HandleAsync(ForgotPasswordCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await directory.FindLiveByEmailAsync(request.Email, ct);
        if (user is null || !user.IsActive)
        {
            return Unit.Value; // Non-enumerating: nothing observable.
        }

        var now = DateTimeOffset.UtcNow;
        var window = await RecentRequestsAsync(user, now);
        if (window.Count >= RecoveryPolicy.MaxRequestsPerHour)
        {
            throw new TooManyRequestsException();
        }

        // Identity's default reset token is a data-protected blob, so nothing
        // is persisted and there is no row to expire or consume — the token
        // carries its own lifetime and is invalidated on use by the security
        // stamp changing. The user id rides in front of it because the wire
        // shape is { token, newPassword } with no account field, and
        // ResetPasswordAsync has to know whose token it is.
        var token = await users.GeneratePasswordResetTokenAsync(user);
        var link = $"{user.Id:N}.{token}";

        try
        {
            // The token is handed to the mailer and NEVER persisted.
            await emailer.SendPasswordResetAsync(user.Email!, link, ct);
        }
        catch (Exception)
        {
            // Compensate the frozen way: an unusable link must not burn a
            // rate-limit slot, so the attempt is simply not recorded. Stay
            // generic — surfacing a 500 only for known accounts would break
            // non-enumeration.
            return Unit.Value;
        }

        window.Add(now);
        try
        {
            await users.SetAuthenticationTokenAsync(
                user,
                RecoveryPolicy.RequestLogProvider,
                RecoveryPolicy.RequestLogName,
                string.Join(';', window.Select(at => at.ToString("O", CultureInfo.InvariantCulture))));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The window is ONE row per account, created on the account's first
            // ever request — so the first two concurrent requests both find no
            // row and both try to insert the same key, and the loser's
            // SaveChanges fails. Losing a slot is fail-open by exactly one
            // request; letting the failure out would be a 500 that happens only
            // for an account that EXISTS and has never asked for a reset, which
            // is an enumeration oracle on the one route that must never have
            // one. The narrower catch this deserves — DbUpdateException — is not
            // available: Hsm.Application must not reference EF Core, and
            // PortPurityTests fails the build if it does.
        }

        return Unit.Value;
    }

    /// <summary>
    /// The account's reset requests inside the ROLLING hour (frozen
    /// account-recovery.service.ts: at most five). The window used to be
    /// inferred from the created-at timestamps of the reset-token rows; with
    /// those rows gone it is kept explicitly, as the most recent request
    /// timestamps in an Identity user token — the same table (user_tokens)
    /// reset state would live in if Identity's own tokens were stateful.
    /// </summary>
    private async Task<List<DateTimeOffset>> RecentRequestsAsync(HsmUser user, DateTimeOffset now)
    {
        var recorded = await users.GetAuthenticationTokenAsync(
            user, RecoveryPolicy.RequestLogProvider, RecoveryPolicy.RequestLogName);

        return
        [
            .. (recorded ?? string.Empty)
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(value => DateTimeOffset.TryParse(
                    value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
                        ? at
                        : (DateTimeOffset?)null)
                .Where(at => at is not null && now - at.Value < RecoveryPolicy.TokenTtl)
                .Select(at => at!.Value),
        ];
    }
}
