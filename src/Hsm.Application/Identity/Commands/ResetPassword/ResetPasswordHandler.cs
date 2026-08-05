using FluentValidation;
using Hsm.Application.Abstractions;
using Hsm.Domain.Identity;
using Microsoft.AspNetCore.Identity;

namespace Hsm.Application.Identity.Commands.ResetPassword;

public sealed class ResetPasswordHandler(UserManager<HsmUser> users)
    : IRequestHandler<ResetPasswordCommand, Unit>
{
    private const string GenericFailure = "The password reset link is invalid or has expired.";

    public async Task<Unit> HandleAsync(ResetPasswordCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        // A malformed link, an unknown account, an expired token and an
        // already-spent token all produce the SAME failure — the reset form
        // must not answer "does this account exist" either.
        var (userId, token) = Split(request.Token);
        var user = userId is null ? null : await users.FindByIdAsync(userId.Value.ToString());
        if (user is null || user.DeletedAt is not null)
        {
            throw Refused();
        }

        var result = await users.ResetPasswordAsync(user, token, request.NewPassword);
        if (!result.Succeeded)
        {
            // A password-policy failure is the caller's fault and is reported
            // as such; anything else is the token, and stays generic.
            if (result.Errors.All(e => e.Code.StartsWith("Password", StringComparison.Ordinal)))
            {
                result.ThrowIfFailed("newPassword");
            }

            throw Refused();
        }

        // Sessions opened before the reset are already dead: ResetPasswordAsync
        // rotates the security stamp, and the cookie validator re-checks it on
        // every request (ValidationInterval = Zero), so the next request any of
        // them makes is a 401. That replaced the explicit refresh-token
        // deactivation this handler used to do — a stronger revocation, because
        // it also covers the sessions that never had a refresh token.
        return Unit.Value;
    }

    /// <summary>
    /// The emailed link is "{userId:N}.{identityToken}" — see
    /// ForgotPasswordHandler for why the id has to ride along.
    /// </summary>
    private static (Guid? UserId, string Token) Split(string link)
    {
        var separator = link?.IndexOf('.', StringComparison.Ordinal) ?? -1;
        if (link is null || separator <= 0)
        {
            return (null, string.Empty);
        }

        return Guid.TryParseExact(link[..separator], "N", out var id)
            ? (id, link[(separator + 1)..])
            : (null, string.Empty);
    }

    private static ValidationException Refused() =>
        new([new FluentValidation.Results.ValidationFailure("token", GenericFailure)]);
}
