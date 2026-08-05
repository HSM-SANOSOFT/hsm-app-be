using Hsm.Application.Abstractions;

namespace Hsm.Application.Identity.Commands.RevokeIntegrationTokens;

public sealed class RevokeIntegrationTokensHandler(IIntegrationRefreshTokenStore tokens)
    : IRequestHandler<RevokeIntegrationTokensCommand, int>
{
    public Task<int> HandleAsync(RevokeIntegrationTokensCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        return tokens.DeactivateActiveAsync(request.AccountId, ct);
    }
}
