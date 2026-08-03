using Hsm.Application.Abstractions;
using Hsm.Application.Errors;

namespace Hsm.Application.Auth.Commands.LogoutIntegration;

public sealed class LogoutIntegrationHandler(
    IAuthTokenCodec codec,
    IIntegrationRefreshTokenStore integrationTokens,
    IAuthUnitOfWork unitOfWork)
    : IRequestHandler<LogoutIntegrationCommand, Unit>
{
    public async Task<Unit> HandleAsync(LogoutIntegrationCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var principal = (await codec.ValidateAsync(request.Token, TokenKind.Access, ignoreExpiration: true)).Principal
            ?? (await codec.ValidateAsync(request.Token, TokenKind.Refresh, ignoreExpiration: true)).Principal
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
        return Unit.Value;
    }
}
