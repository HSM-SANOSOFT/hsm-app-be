using Hsm.Application.Abstractions;
using Hsm.Application.Errors;

namespace Hsm.Application.Auth.Commands.LogoutIntegration;

public sealed class LogoutIntegrationHandler(
    IIntegrationTokenCodec codec,
    IIntegrationRefreshTokenStore refreshTokens,
    IAuthUnitOfWork unitOfWork)
    : IRequestHandler<LogoutIntegrationCommand, Unit>
{
    public async Task<Unit> HandleAsync(LogoutIntegrationCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var accountId = await IdentifyAsync(request.Token, ct)
            ?? throw new UnauthorizedException("Invalid token");

        var affected = await refreshTokens.DeactivateActiveAsync(accountId, ct);
        if (affected == 0)
        {
            throw new ConflictException("No active session to end.");
        }

        await unitOfWork.SaveChangesAsync(ct);
        return Unit.Value;
    }

    /// <summary>
    /// Which account the presented token belongs to, by the only two routes
    /// there are: a signed access token SAYS so; an opaque refresh token matches
    /// a stored digest.
    ///
    /// <para>Expiry is ignored on the access token deliberately — revoking the
    /// credential of an account whose access token lapsed an hour ago is exactly
    /// what an operator reaching for this route is trying to do.</para>
    /// </summary>
    private async Task<Guid?> IdentifyAsync(string token, CancellationToken ct)
    {
        var principal = (await codec.ValidateAsync(token, ignoreExpiration: true)).Principal;
        if (principal is not null)
        {
            // A signed token that is NOT an integration's is refused rather than
            // fallen through on: it is a real credential aimed at the wrong kind
            // of subject, and there is nothing here to revoke for it.
            return principal.IsIntegration ? Guid.Parse(principal.Id) : null;
        }

        return await refreshTokens.FindActiveAccountByHashAsync(
            IntegrationTokenIssuer.HashRefreshToken(token), ct);
    }
}
