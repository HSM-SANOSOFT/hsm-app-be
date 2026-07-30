using Hsm.Application.Abstractions;
using Hsm.Domain.Identity;

namespace Hsm.Application.Auth.Commands.Signup;

public sealed class SignupHandler(
    IUserStore users,
    IPasswordHasher hasher,
    TokenIssuer issuer,
    IUserRefreshTokenStore userTokens,
    IAuthUnitOfWork unitOfWork)
    : IRequestHandler<SignupCommand, TokenPair>
{
    public async Task<TokenPair> HandleAsync(SignupCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = request.Username,
            Email = request.Email,
            PasswordHash = hasher.Hash(request.Password),
            FirstName = request.FirstName,
            FirstLastName = request.FirstLastName,
            SecondName = request.SecondName,
            SecondLastName = request.SecondLastName,
            PhoneNumber = request.PhoneNumber,
            Gender = request.Gender,
            OnboardingCompletedAt = DateTimeOffset.UtcNow,
        };
        var principal = TokenIssuer.PrincipalFor(user) with { Roles = [Roles.Patient] };
        var tokens = issuer.GenerateTokens(principal);
        // Pre-digest before bcrypt — see TokenDigests.
        var refreshHash = hasher.Hash(TokenDigests.Sha256Hex(tokens.RefreshToken));

        // TransactionBehavior owns the boundary the frozen handler opened here.
        await users.AddAsync(user, [Roles.Patient], ct);
        await userTokens.AddAsync(user.Id, refreshHash, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return tokens;
    }
}
