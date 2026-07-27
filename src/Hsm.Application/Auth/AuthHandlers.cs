using Hsm.Application.Errors;
using Hsm.Domain.Identity;

namespace Hsm.Application.Auth;

/// <summary>Username/password sign-in (frozen validateUser + login). Split in
/// two because the frozen guard validated credentials BEFORE body validation
/// ran, and tokens were only issued afterwards.</summary>
public sealed class LoginHandler(IUserStore users, IPasswordHasher hasher, TokenIssuer issuer)
{
    public async Task<AuthPrincipal> ValidateCredentialsAsync(string username, string password, CancellationToken ct = default)
    {
        // Unknown username and wrong password surface the SAME coded error —
        // the login form must not leak which accounts exist.
        var user = await users.FindByUsernameAsync(username, ct)
            ?? throw ApiException.Unauthorized("Invalid credentials", ApiErrorCode.InvalidCredentials);

        if (!hasher.Verify(password, user.PasswordHash))
        {
            throw ApiException.Unauthorized("Invalid password", ApiErrorCode.InvalidCredentials);
        }

        return TokenIssuer.PrincipalFor(user);
    }

    public Task<TokenPair> IssueAsync(AuthPrincipal principal, CancellationToken ct = default) =>
        issuer.IssueAsync(principal, ct);
}

/// <summary>
/// Public self-registration. The account is ALWAYS a Patient — client-supplied
/// roles are discarded — and created onboarding-complete (patients never do
/// the staff first-login flow).
/// </summary>
public sealed class SignupHandler(
    IUserStore users,
    IPasswordHasher hasher,
    TokenIssuer issuer,
    IUserRefreshTokenStore userTokens,
    IAuthUnitOfWork unitOfWork)
{
    public sealed record Command(
        string Username,
        string Email,
        string Password,
        string FirstName,
        string FirstLastName,
        string? SecondName,
        string? SecondLastName,
        string? PhoneNumber,
        string? Gender);

    public async Task<TokenPair> HandleAsync(Command command, CancellationToken ct = default)
    {
        return await unitOfWork.ExecuteInTransactionAsync(
            async innerCt =>
            {
                var user = new User
                {
                    Id = Guid.NewGuid(),
                    Username = command.Username,
                    Email = command.Email,
                    PasswordHash = hasher.Hash(command.Password),
                    FirstName = command.FirstName,
                    FirstLastName = command.FirstLastName,
                    SecondName = command.SecondName,
                    SecondLastName = command.SecondLastName,
                    PhoneNumber = command.PhoneNumber,
                    Gender = command.Gender,
                    OnboardingCompletedAt = DateTimeOffset.UtcNow,
                };
                await users.AddAsync(user, [Roles.Patient], innerCt);

                var principal = new AuthPrincipal
                {
                    Id = user.Id.ToString(),
                    Username = user.Username,
                    Email = user.Email,
                    FirstName = user.FirstName,
                    FirstLastName = user.FirstLastName,
                    Roles = [Roles.Patient],
                    OnboardingCompletedAt = TokenIssuer.SerializeOnboarding(user.OnboardingCompletedAt),
                    HasOnboardingClaim = true,
                };
                var tokens = issuer.GenerateTokens(principal);
                await userTokens.AddAsync(user.Id, HashOf(tokens.RefreshToken), innerCt);
                await unitOfWork.SaveChangesAsync(innerCt);
                return tokens;
            },
            ct);
    }

    private string HashOf(string refreshToken) => hasher.Hash(TokenDigests.Sha256Hex(refreshToken));
}

/// <summary>
/// Refresh rotation (frozen validateRefreshToken + refresh): the presented
/// token must match the single active hash in the mode's own store; success
/// rotates, so the prior token stops working.
/// </summary>
public sealed class RefreshHandler(
    IUserRefreshTokenStore userTokens,
    IIntegrationRefreshTokenStore integrationTokens,
    IPasswordHasher hasher,
    TokenIssuer issuer)
{
    public async Task<TokenPair> HandleAsync(AuthPrincipal principal, string rawRefreshToken, CancellationToken ct = default)
    {
        var id = Guid.Parse(principal.Id);
        string? activeHash = principal.IsIntegration
            ? (await integrationTokens.FindActiveAsync(id, ct))?.TokenHash
            : (await userTokens.FindActiveAsync(id, ct))?.TokenHash;

        if (activeHash is null)
        {
            throw ApiException.Unauthorized("Active Refresh token not found");
        }

        if (!hasher.Verify(TokenDigests.Sha256Hex(rawRefreshToken), activeHash))
        {
            throw ApiException.Unauthorized("Refresh token is not valid");
        }

        // Reissue from the token's own claims, minus iat/exp.
        var toSign = principal with { IssuedAt = null, ExpiresAt = null };
        return await issuer.IssueAsync(toSign, ct);
    }
}

/// <summary>
/// Sign-out (frozen logout): accepts an access OR refresh token (expired is
/// fine — sign-out must always be possible) and deactivates the USER store
/// rows for its subject.
/// </summary>
public sealed class LogoutHandler(
    IAuthTokenCodec codec,
    IUserRefreshTokenStore userTokens,
    IAuthUnitOfWork unitOfWork)
{
    public async Task HandleAsync(string? token, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(token))
        {
            throw ApiException.Unauthorized("Token not found");
        }

        var principal = (await codec.ValidateAsync(token, TokenKind.Access, ignoreExpiration: true)).Principal
            ?? (await codec.ValidateAsync(token, TokenKind.Refresh, ignoreExpiration: true)).Principal
            ?? throw ApiException.Unauthorized("Invalid token");

        var affected = await userTokens.DeactivateActiveAsync(Guid.Parse(principal.Id), ct);
        if (affected == 0)
        {
            throw ApiException.BadRequest("already logged out");
        }

        await unitOfWork.SaveChangesAsync(ct);
    }
}

/// <summary>Admin-driven integration account provisioning (frozen signupIntegration).</summary>
public sealed class SignupIntegrationHandler(
    IIntegrationAccountStore accounts,
    IIntegrationRefreshTokenStore integrationTokens,
    IPasswordHasher hasher,
    TokenIssuer issuer,
    IAuthUnitOfWork unitOfWork)
{
    public sealed record Command(string Name, string Description, string Functionality);

    public async Task<TokenPair> HandleAsync(Command command, CancellationToken ct = default)
    {
        return await unitOfWork.ExecuteInTransactionAsync(
            async innerCt =>
            {
                var account = new IntegrationAccount
                {
                    Id = Guid.NewGuid(),
                    Name = command.Name,
                    Description = command.Description,
                    Functionality = command.Functionality,
                };
                await accounts.AddAsync(account, innerCt);

                var principal = new AuthPrincipal
                {
                    Id = account.Id.ToString(),
                    Name = account.Name,
                    Roles = [Roles.Integration],
                };
                var tokens = issuer.GenerateTokens(principal);
                await integrationTokens.AddAsync(
                    account.Id, hasher.Hash(TokenDigests.Sha256Hex(tokens.RefreshToken)), innerCt);
                await unitOfWork.SaveChangesAsync(innerCt);
                return tokens;
            },
            ct);
    }
}

/// <summary>Admin-driven integration sign-out (frozen logoutIntegration).</summary>
public sealed class LogoutIntegrationHandler(
    IAuthTokenCodec codec,
    IIntegrationRefreshTokenStore integrationTokens,
    IAuthUnitOfWork unitOfWork)
{
    public async Task HandleAsync(string token, CancellationToken ct = default)
    {
        var principal = (await codec.ValidateAsync(token, TokenKind.Access, ignoreExpiration: true)).Principal
            ?? (await codec.ValidateAsync(token, TokenKind.Refresh, ignoreExpiration: true)).Principal
            ?? throw ApiException.Unauthorized("Invalid token");

        if (!principal.IsIntegration)
        {
            throw ApiException.Unauthorized("Not an integration token");
        }

        var affected = await integrationTokens.DeactivateActiveAsync(Guid.Parse(principal.Id), ct);
        if (affected == 0)
        {
            throw ApiException.BadRequest("already logged out");
        }

        await unitOfWork.SaveChangesAsync(ct);
    }
}

/// <summary>
/// First-login onboarding completion for a pending staff account (frozen
/// completeOnboarding): confirm the email, set password + phone atomically,
/// clear the pending flag, and reissue tokens so the pre-onboarding refresh
/// token cannot be replayed.
/// </summary>
public sealed class CompleteOnboardingHandler(
    IUserStore users,
    IPasswordHasher hasher,
    TokenIssuer issuer,
    IAuthUnitOfWork unitOfWork)
{
    public sealed record Command(string NewPassword, string PhoneNumber, string ConfirmEmail);

    public async Task<TokenPair> HandleAsync(Guid userId, Command command, CancellationToken ct = default)
    {
        var user = await users.FindByIdAsync(userId, ct)
            ?? throw ApiException.NotFound($"User with id {userId} not found");

        if (!string.Equals(command.ConfirmEmail, user.Email, StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.BadRequest("Confirmation email does not match the account email");
        }

        if (user.OnboardingCompletedAt is not null)
        {
            throw ApiException.BadRequest("Onboarding already completed");
        }

        user.PasswordHash = hasher.Hash(command.NewPassword);
        user.PhoneNumber = command.PhoneNumber;
        user.EmailVerified = true;
        user.OnboardingCompletedAt = DateTimeOffset.UtcNow;
        await unitOfWork.SaveChangesAsync(ct);

        return await issuer.IssueAsync(TokenIssuer.PrincipalFor(user), ct);
    }
}

/// <summary>
/// The frozen PIN endpoints are inert stubs — generation/validation log and
/// persist nothing. Behavioral parity means preserving exactly that: an
/// authenticated 2xx no-op. (The real attempt-throttling/lockout behavior in
/// the frozen system lives in account recovery — see AccountRecoveryHandlers.)
/// </summary>
public static class PinHandlers
{
    public static Task GenerateAsync(string purpose, string target) => Task.CompletedTask;

    public static Task ValidateAsync(string purpose, string target, double code) => Task.CompletedTask;
}
