using Hsm.Application.Abstractions;
using Hsm.Application.Errors;
using Hsm.Domain.Identity;
using Microsoft.AspNetCore.Identity;

namespace Hsm.Application.Identity.Commands.Login;

public sealed class LoginHandler(
    UserManager<HsmUser> users,
    IUserDirectory directory)
    : IRequestHandler<LoginCommand, HsmUser>
{
    public async Task<HsmUser> HandleAsync(LoginCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Unknown username, soft-deleted account, DEACTIVATED account, wrong
        // password and lockout all surface the SAME message — the login form
        // must not leak which accounts exist, which are currently locked out,
        // nor which have been switched off.
        // Through the directory, not UserManager.FindByNameAsync: a soft-deleted
        // row may share this name with the live account, and the unfiltered
        // lookup can return either one.
        var user = await directory.FindLiveByNameAsync(request.Username, ct);
        if (user is null)
        {
            throw new UnauthorizedException("Invalid username or password.");
        }

        // Deactivation is the admin's off switch for an account that still
        // exists — HsmUser.IsActive, on the wire as UserResource.isActive and
        // rendered on the admin screen. IssueIntegrationTokensHandler and
        // RefreshIntegrationTokensHandler already refuse on it; a human sign-in
        // has to as well, or the switch does nothing to the door people
        // actually use. Checked before the password so a deactivated account's
        // failures never touch the lockout counter.
        if (!user.IsActive)
        {
            throw new UnauthorizedException("Invalid username or password.");
        }

        // Lockout is checked FIRST among the credential checks: a locked account
        // must be refused even when the password presented is the right one, or
        // the lockout buys nothing.
        if (await users.IsLockedOutAsync(user))
        {
            throw new UnauthorizedException("Invalid username or password.");
        }

        if (!await users.CheckPasswordAsync(user, request.Password))
        {
            // Counts toward the lockout threshold configured in AddHsmIdentity.
            await users.AccessFailedAsync(user);
            throw new UnauthorizedException("Invalid username or password.");
        }

        await users.ResetAccessFailedCountAsync(user);

        // The verified account, and nothing else. What a successful sign-in
        // then hands the caller — a session cookie at the REST door and at the
        // shell — is transport, and each door writes its own.
        return user;
    }
}
