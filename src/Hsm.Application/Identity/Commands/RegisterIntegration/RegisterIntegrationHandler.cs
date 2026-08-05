using Hsm.Application.Abstractions;
using Hsm.Domain.Identity;

namespace Hsm.Application.Identity.Commands.RegisterIntegration;

public sealed class RegisterIntegrationHandler(
    IIntegrationAccountStore accounts,
    IntegrationTokenIssuer issuer)
    : IRequestHandler<RegisterIntegrationCommand, IntegrationTokens>
{
    public async Task<IntegrationTokens> HandleAsync(RegisterIntegrationCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var account = new IntegrationAccount
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description,
            Functionality = request.Functionality,
        };
        await accounts.AddAsync(account, ct);

        // The account is only TRACKED here; the issuer's SaveChanges is what
        // writes it, alongside the refresh-token row that points at it. Both
        // land in the transaction TransactionBehavior opened, so an account can
        // never exist without the credential that was reported for it — nor a
        // credential without its account.
        return await issuer.IssueAsync(
            new AuthPrincipal
            {
                Id = account.Id.ToString(),
                Name = account.Name,
                Roles = [Roles.Integration],
            },
            ct);
    }
}
