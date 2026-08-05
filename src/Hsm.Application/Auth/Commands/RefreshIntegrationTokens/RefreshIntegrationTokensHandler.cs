using Hsm.Application.Abstractions;
using Hsm.Application.Errors;
using Hsm.Domain.Identity;

namespace Hsm.Application.Auth.Commands.RefreshIntegrationTokens;

public sealed class RefreshIntegrationTokensHandler(
    IIntegrationRefreshTokenStore refreshTokens,
    IIntegrationAccountStore accounts,
    IAuthUnitOfWork unitOfWork,
    IntegrationTokenIssuer issuer)
    : IRequestHandler<RefreshIntegrationTokensCommand, IntegrationTokens>
{
    /// <summary>
    /// ONE answer for every way this can fail — never existed, already spent,
    /// belongs to a revoked or deleted account. They are the same fact to a
    /// caller (this token is not redeemable), and distinguishing them would tell
    /// whoever holds a stolen token whether it was ever real, or whether their
    /// replay tripped anything.
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
        var accountId = await refreshTokens.FindActiveAccountByHashAsync(hash, ct);
        if (accountId is null)
        {
            await RaiseTheAlarmIfSpentAsync(hash, ct);
            throw new UnauthorizedException(Refusal);
        }

        // Rotation is all-or-nothing, so it gets a transaction: if the insert
        // failed after the claim, the account would be left with its credential
        // revoked and no replacement, and its own next call could not recover.
        //
        // This handler opens that transaction rather than riding the pipeline's
        // (see [NoAmbientTransaction] on the command), because the alarm above
        // must OUTLIVE the refusal it triggers — and an ambient transaction
        // would roll the revocation back with the very exception that reported
        // it.
        return await unitOfWork.ExecuteInTransactionAsync(
            async innerCt => await RotateAsync(hash, accountId.Value, innerCt), ct);
    }

    /// <summary>
    /// REUSE DETECTION. The presented digest matches no ACTIVE row — but if it
    /// matches a SPENT one it was real once, which means two parties hold copies
    /// of a credential the account has since rotated past: the legitimate
    /// integration, and whoever took it.
    ///
    /// <para>Nothing here can tell which one is asking, so the only safe answer
    /// is to trust neither and revoke what is currently live. That is
    /// deliberately disruptive. Without it, the first party to redeem a stolen
    /// token simply BECOMES the account: the legitimate holder's next refresh
    /// fails exactly like an ordinary expiry, nothing is alerted, and the
    /// takeover is silent and permanent. Killing the chain makes the legitimate
    /// holder fail too — loudly, immediately, and in a way that reaches a human
    /// — and re-provisioning is a cheap price for the one case this fires
    /// in.</para>
    ///
    /// <para>The caller still gets the ordinary refusal. Telling an attacker
    /// that their replay tripped an alarm is a gift.</para>
    ///
    /// <para>It costs one indexed read, and only on the path that was already
    /// going to fail.</para>
    /// </summary>
    private async Task RaiseTheAlarmIfSpentAsync(string hash, CancellationToken ct)
    {
        if (await refreshTokens.FindAccountByHashAsync(hash, ct) is { } compromised)
        {
            await refreshTokens.DeactivateActiveAsync(compromised, ct);
        }
    }

    private async Task<IntegrationTokens> RotateAsync(
        string hash, Guid accountId, CancellationToken ct)
    {
        // THE RACE GUARD, and the reason this is a conditional write rather than
        // the read in HandleAsync being enough. Two requests presenting the same
        // token both see the row there; this UPDATE is what settles which of
        // them owns it. The second blocks on the first's row lock, re-evaluates
        // `is_active` after it commits, and matches nothing.
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
