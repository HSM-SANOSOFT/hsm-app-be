using Hsm.Application.Abstractions;
using Hsm.Application.Errors;

namespace Hsm.Application.Auth.Commands.Login;

public sealed class LoginHandler(IUserStore users, IPasswordHasher hasher, TokenIssuer issuer)
    : IRequestHandler<LoginCommand, TokenPair>
{
    public async Task<TokenPair> HandleAsync(LoginCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Unknown username and wrong password surface the SAME message — the
        // login form must not leak which accounts exist.
        var user = await users.FindByUsernameAsync(request.Username, ct)
            ?? throw new UnauthorizedException("Invalid username or password.");

        if (!hasher.Verify(request.Password, user.PasswordHash))
        {
            throw new UnauthorizedException("Invalid username or password.");
        }

        return await issuer.IssueAsync(TokenIssuer.PrincipalFor(user), ct);
    }
}
