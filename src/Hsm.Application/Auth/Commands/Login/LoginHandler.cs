using Hsm.Application.Abstractions;
using Hsm.Application.Errors;
using Hsm.Domain.Identity;
using Microsoft.AspNetCore.Identity;

namespace Hsm.Application.Auth.Commands.Login;

public sealed class LoginHandler(UserManager<HsmUser> users, TokenIssuer issuer)
    : IRequestHandler<LoginCommand, TokenPair>
{
    public async Task<TokenPair> HandleAsync(LoginCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Unknown username, soft-deleted account, wrong password and lockout all
        // surface the SAME message — the login form must not leak which accounts
        // exist, nor which of them are currently locked out.
        var user = await users.FindByNameAsync(request.Username);
        if (user is null || user.DeletedAt is not null)
        {
            throw new UnauthorizedException("Invalid username or password.");
        }

        // Lockout is checked FIRST: a locked account must be refused even when
        // the password presented is the right one, or the lockout buys nothing.
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
        return await issuer.IssueAsync(
            TokenIssuer.PrincipalFor(user, [.. await users.GetRolesAsync(user)]), ct);
    }
}
