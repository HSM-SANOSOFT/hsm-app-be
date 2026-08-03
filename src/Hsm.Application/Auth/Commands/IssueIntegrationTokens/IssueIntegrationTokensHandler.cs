using Hsm.Application.Abstractions;
using Hsm.Application.Errors;
using Hsm.Domain.Identity;

namespace Hsm.Application.Auth.Commands.IssueIntegrationTokens;

public sealed class IssueIntegrationTokensHandler(IIntegrationAccountStore accounts, TokenIssuer issuer)
    : IRequestHandler<IssueIntegrationTokensCommand, TokenPair>
{
    public async Task<TokenPair> HandleAsync(IssueIntegrationTokensCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var account = await accounts.FindByIdAsync(request.AccountId, ct)
            ?? throw ApiException.NotFound($"Integration account with id {request.AccountId} not found");

        if (!account.IsActive)
        {
            throw ApiException.BadRequest("Integration account is inactive");
        }

        var principal = new AuthPrincipal
        {
            Id = account.Id.ToString(),
            Name = account.Name,
            Roles = [Roles.Integration],
        };
        return await issuer.IssueAsync(principal, ct);
    }
}
