using Hsm.Application.Abstractions;
using Hsm.Application.Errors;

namespace Hsm.Application.Identity.Commands.LogoutIntegration;

public sealed class LogoutIntegrationHandler(
    IIntegrationTokenCodec codec,
    IIntegrationRefreshTokenStore refreshTokens,
    IUnitOfWork unitOfWork)
    : IRequestHandler<LogoutIntegrationCommand, Unit>
{
    public async Task<Unit> HandleAsync(LogoutIntegrationCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await RevokeAsync(request.Token, ct) == 0)
        {
            throw new ConflictException("No active session to end.");
        }

        await unitOfWork.SaveChangesAsync(ct);
        return Unit.Value;
    }

    /// <summary>
    /// Revokes whatever the presented token's account currently holds, and
    /// returns how many rows that was. Two ways in, because either half of the
    /// pair identifies an account: a signed access token SAYS which; an opaque
    /// refresh token MATCHES a stored digest.
    ///
    /// <para>Expiry is ignored on the access token deliberately — revoking the
    /// credential of an account whose access token lapsed an hour ago is exactly
    /// what an operator reaching for this route is trying to do.</para>
    /// </summary>
    private async Task<int> RevokeAsync(string token, CancellationToken ct)
    {
        var principal = (await codec.ValidateAsync(token, ignoreExpiration: true)).Principal;
        if (principal is not null)
        {
            // A signed token that is NOT an integration's is refused rather than
            // fallen through on: it is a real credential aimed at the wrong kind
            // of subject, and there is nothing here to revoke for it.
            if (!principal.IsIntegration)
            {
                throw new UnauthorizedException("Invalid token");
            }

            // No digest to key on — an access token names an account, not a
            // specific refresh row — so this is inherently "kill whatever is
            // live for this account". The store's own retry is what keeps that
            // honest against a concurrent rotation; see DeactivateActiveAsync.
            return await refreshTokens.DeactivateActiveAsync(Guid.Parse(principal.Id), ct);
        }

        // Key on the DIGEST first, so an admin revoking the exact token they
        // were handed contends on the same row a concurrent refresh would —
        // rather than issuing an account-scoped update that can lose that race
        // and report "nothing to revoke" while a successor goes live.
        var hash = IntegrationTokenIssuer.HashRefreshToken(token);
        if (await refreshTokens.ClaimActiveAsync(hash, ct) == 1)
        {
            return 1;
        }

        // The presented token is not the live one. Either it was never issued —
        // a guess, refused — or the account has rotated past it, which is also
        // exactly what losing that race looks like from here. In both of those
        // last cases the operator's intent is unchanged: this account's access
        // is to end, so kill the successor too.
        var accountId = await refreshTokens.FindAccountByHashAsync(hash, ct)
            ?? throw new UnauthorizedException("Invalid token");

        return await refreshTokens.DeactivateActiveAsync(accountId, ct);
    }
}
