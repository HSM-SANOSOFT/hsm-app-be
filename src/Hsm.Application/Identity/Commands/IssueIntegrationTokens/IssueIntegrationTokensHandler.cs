using Hsm.Application.Abstractions;
using Hsm.Application.Errors;
using Hsm.Domain.Identity;

namespace Hsm.Application.Identity.Commands.IssueIntegrationTokens;

public sealed class IssueIntegrationTokensHandler(
    IIntegrationAccountStore accounts, IntegrationTokenIssuer issuer)
    : IRequestHandler<IssueIntegrationTokensCommand, IntegrationTokens>
{
    public async Task<IntegrationTokens> HandleAsync(
        IssueIntegrationTokensCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var account = await accounts.FindByIdAsync(request.AccountId, ct)
            ?? throw new NotFoundException("IntegrationAccount", request.AccountId);

        if (!account.IsActive)
        {
            throw new ConflictException("Integration account is not active.");
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
