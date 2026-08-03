using Hsm.Application.Abstractions;
using Hsm.Domain.Identity;

namespace Hsm.Application.Auth.Commands.SignupIntegration;

public sealed class SignupIntegrationHandler(
    IIntegrationAccountStore accounts,
    IIntegrationRefreshTokenStore integrationTokens,
    IPasswordHasher hasher,
    TokenIssuer issuer,
    IAuthUnitOfWork unitOfWork)
    : IRequestHandler<SignupIntegrationCommand, TokenPair>
{
    public async Task<TokenPair> HandleAsync(SignupIntegrationCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var account = new IntegrationAccount
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description,
            Functionality = request.Functionality,
        };
        var principal = new AuthPrincipal
        {
            Id = account.Id.ToString(),
            Name = account.Name,
            Roles = [Roles.Integration],
        };
        var tokens = issuer.GenerateTokens(principal);
        // Pre-digest before bcrypt — see TokenDigests.
        var refreshHash = hasher.Hash(TokenDigests.Sha256Hex(tokens.RefreshToken));

        // TransactionBehavior owns the boundary the frozen handler opened here.
        await accounts.AddAsync(account, ct);
        await integrationTokens.AddAsync(account.Id, refreshHash, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return tokens;
    }
}
