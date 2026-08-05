using Hsm.Application.Abstractions;
using Hsm.Application.Errors;
using Hsm.Domain.Identity;

namespace Hsm.Application.Auth.Commands.RefreshIntegrationTokens;

public sealed class RefreshIntegrationTokensHandler(
    IIntegrationRefreshTokenStore refreshTokens,
    IIntegrationAccountStore accounts,
    IntegrationTokenIssuer issuer)
    : IRequestHandler<RefreshIntegrationTokensCommand, IntegrationTokens>
{
    /// <summary>
    /// ONE answer for every way this can fail — never existed, already spent,
    /// belongs to a revoked or deleted account. They are the same fact to a
    /// caller (this token is not redeemable), and distinguishing them would tell
    /// whoever holds a stolen token whether it was ever real.
    /// </summary>
    private const string Refusal = "Invalid refresh token.";

    public async Task<IntegrationTokens> HandleAsync(
        RefreshIntegrationTokensCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The token says nothing about itself, so the digest is the only way to
        // learn whose it is. There is no signature to check first and no claim
        // to distrust: matching a row IS the verification.
        var hash = IntegrationTokenIssuer.HashRefreshToken(request.RawRefreshToken);
        var accountId = await refreshTokens.FindActiveAccountByHashAsync(hash, ct)
            ?? throw new UnauthorizedException(Refusal);

        // THE RACE GUARD, and the reason this is a conditional write rather than
        // the read above being enough. Two requests presenting the same token
        // both see the row here; the UPDATE is what settles which of them owns
        // it. Both run inside the transaction TransactionBehavior opened, so the
        // second blocks on the first's row lock, re-evaluates `is_active` after
        // it commits, and matches nothing.
        //
        // Deactivating by ACCOUNT instead would not do it: the loser's statement
        // would find no active row for the account, report zero, and go on to
        // insert a second live token anyway — one presented token, two working
        // credentials.
        if (await refreshTokens.ClaimActiveAsync(hash, ct) != 1)
        {
            throw new UnauthorizedException(Refusal);
        }

        // A revoked or deleted account keeps no live credential: FindByIdAsync
        // filters soft-deleted rows, and IsActive is the admin screen's off
        // switch. Same refusal as an unknown token — the account's existence is
        // not this route's to disclose.
        var account = await accounts.FindByIdAsync(accountId, ct);
        if (account is null || !account.IsActive)
        {
            throw new UnauthorizedException(Refusal);
        }

        // Rebuilt from the ROW, not from anything presented. The old JWT refresh
        // token re-signed its own claims, which is why it had to refuse humans
        // explicitly: a demoted user could otherwise renew a stale role forever.
        // Nothing here can carry a claim across, because nothing was presented
        // that had one.
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
