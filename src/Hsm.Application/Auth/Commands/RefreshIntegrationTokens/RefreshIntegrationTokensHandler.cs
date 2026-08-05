using Hsm.Application.Abstractions;
using Hsm.Application.Errors;
using Hsm.Domain.Identity;
using Microsoft.Extensions.Logging;

namespace Hsm.Application.Auth.Commands.RefreshIntegrationTokens;

public sealed partial class RefreshIntegrationTokensHandler(
    IIntegrationRefreshTokenStore refreshTokens,
    IIntegrationAccountStore accounts,
    IAuthUnitOfWork unitOfWork,
    IntegrationTokenIssuer issuer,
    ILogger<RefreshIntegrationTokensHandler> logger)
    : IRequestHandler<RefreshIntegrationTokensCommand, IntegrationTokens>
{
    /// <summary>
    /// How long after a digest is spent a replay of it is read as a client
    /// colliding with ITSELF rather than as theft.
    ///
    /// <para>The trigger it exists for is a machine client with no mutex around
    /// its own refresh call: two threads both notice the access token is near
    /// expiry, both present the same token, and the loser arrives milliseconds
    /// after the winner spent it. Without forgiveness that ordinary scheduling
    /// overlap revokes the credential the winner just minted and takes a human
    /// to undo.</para>
    ///
    /// <para>Thirty seconds, not sixty. A self-collision resolves in
    /// milliseconds, so this is already four orders of magnitude of headroom,
    /// and every extra second is a second in which a real replay goes
    /// unnoticed — see <see cref="RaiseTheAlarmIfSpentAsync"/> for exactly how
    /// wide that gap is.</para>
    /// </summary>
    private static readonly TimeSpan ReuseGrace = TimeSpan.FromSeconds(30);

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
    /// <para>The caller still gets the ordinary refusal either way. Telling an
    /// attacker that their replay tripped an alarm is a gift.</para>
    ///
    /// <para><b>The one exemption</b> is <see cref="ReuseGrace"/>, and it is
    /// deliberately narrow: only the account's MOST RECENTLY spent digest, and
    /// only for seconds after it was spent. Anything older, and any digest with
    /// a newer rotation behind it, still trips.</para>
    ///
    /// <para>What that costs, stated plainly rather than waved at: a theft goes
    /// unnoticed if the victim's own next refresh happens to land inside the
    /// same thirty seconds as the thief's redemption. The victim refreshes on
    /// its own schedule against a day-long access token, so that overlap is
    /// vanishingly unlikely — and every LATER refresh still trips, because by
    /// then the digest it presents is no longer the most recent one. The
    /// detection is delayed in that corner, never lost.</para>
    ///
    /// <para>Cost: one indexed read always, a second one only when a spent
    /// digest is actually found — both on a path that was already going to
    /// fail.</para>
    /// </summary>
    private async Task RaiseTheAlarmIfSpentAsync(string hash, CancellationToken ct)
    {
        if (await refreshTokens.FindAccountByHashAsync(hash, ct) is not { } compromised)
        {
            // Never issued. A guess, not a replay — nothing to revoke and
            // nothing to report.
            return;
        }

        var newest = await refreshTokens.FindMostRecentlySpentAsync(compromised, ct);
        if (newest is not null
            && string.Equals(newest.TokenHash, hash, StringComparison.Ordinal)
            && DateTimeOffset.UtcNow - newest.UpdatedAt <= ReuseGrace)
        {
            return;
        }

        // Logged BEFORE the revocation, and at Warning: this is the only signal
        // a human gets. The alternative is an integration that silently stops
        // working and an operator with nothing to search for. The account id is
        // the whole point of the message — it is what makes the report
        // actionable — and no digest or token is logged, because a log is
        // exactly where a credential must not end up.
        LogRefreshTokenReuseDetected(logger, compromised, newest?.UpdatedAt);
        await refreshTokens.DeactivateActiveAsync(compromised, ct);
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Integration refresh token REUSE detected for account {IntegrationAccountId}: a "
            + "digest that was already spent was presented again (last rotation {LastRotatedAt}). "
            + "The account's active credential has been revoked and it must be re-provisioned. "
            + "Treat this as a possible credential compromise.")]
    private static partial void LogRefreshTokenReuseDetected(
        ILogger logger, Guid integrationAccountId, DateTimeOffset? lastRotatedAt);

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
