using Hsm.Application.Abstractions;
using Hsm.Domain.Identity;
using Microsoft.AspNetCore.Identity;

namespace Hsm.Application.Auth.Commands.Signup;

public sealed class SignupHandler(
    UserManager<HsmUser> users,
    TokenIssuer issuer,
    IUserRefreshTokenStore userTokens,
    IAuthUnitOfWork unitOfWork)
    : IRequestHandler<SignupCommand, TokenPair>
{
    public async Task<TokenPair> HandleAsync(SignupCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = DateTimeOffset.UtcNow;
        var user = new HsmUser
        {
            Id = Guid.NewGuid(),
            UserName = request.Username,
            Email = request.Email,
            FirstName = request.FirstName,
            FirstLastName = request.FirstLastName,
            SecondName = request.SecondName,
            SecondLastName = request.SecondLastName,
            PhoneNumber = request.PhoneNumber,
            Gender = request.Gender,
            // Patients never do the staff first-login flow.
            OnboardingCompletedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };

        // TransactionBehavior owns the boundary the frozen handler opened here,
        // and UserManager's store shares this scope's DbContext — so the user
        // row, its role row and the refresh-token row commit or roll back as one.
        (await users.CreateAsync(user, request.Password)).ThrowIfFailed("password");
        (await users.AddToRoleAsync(user, Roles.Patient)).ThrowIfFailed("role");

        var principal = TokenIssuer.PrincipalFor(user, [Roles.Patient]);
        var tokens = issuer.GenerateTokens(principal);
        await userTokens.AddAsync(user.Id, TokenIssuer.HashRefreshToken(tokens.RefreshToken), ct);
        await unitOfWork.SaveChangesAsync(ct);
        return tokens;
    }
}
