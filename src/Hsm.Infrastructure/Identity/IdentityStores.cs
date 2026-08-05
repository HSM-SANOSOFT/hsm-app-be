using Hsm.Application.Identity;
using Hsm.Domain.Identity;
using Hsm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Hsm.Infrastructure.Identity;

/// <summary>
/// EF Core adapter for the open-session table.
///
/// <para>Every method here writes or reads through <c>ExecuteUpdate</c>/
/// <c>ExecuteDelete</c> or a no-tracking key lookup rather than through the
/// change tracker: these run inside the COOKIE VALIDATION path, outside any
/// request the pipeline opened a transaction for, so there is no ambient
/// <c>SaveChanges</c> to ride on and nothing may be left pending in a context
/// that is about to be reused by the request proper.</para>
/// </summary>
public sealed class UserSessionStore(HsmDbContext db) : IUserSessionStore
{
    public async Task OpenAsync(
        Guid sessionId, Guid userId, DateTimeOffset expiresAt, CancellationToken ct = default)
    {
        // Reclaim this user's dead rows on the way in. It is the cheapest place
        // to do it — one indexed delete on an account that is already being
        // written to — and it means the table needs no scheduled sweeper.
        await db.UserSessions
            .Where(s => s.UserId == userId && s.ExpiresAt <= DateTimeOffset.UtcNow)
            .ExecuteDeleteAsync(ct);

        db.UserSessions.Add(new UserSession
        {
            Id = sessionId,
            UserId = userId,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = expiresAt,
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> TouchAsync(
        Guid sessionId, Guid userId, DateTimeOffset expiresAt, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        // One statement, not a read followed by a conditional write: the WHERE
        // clause IS the session check (open, owned by this caller, backed by a
        // live and active account) and the SET is the sliding extension. Rows
        // affected therefore answers both questions at once — 0 means the
        // cookie is worthless and nothing was extended.
        return await db.UserSessions
            .Where(s => s.Id == sessionId
                && s.UserId == userId
                && s.ExpiresAt > now
                && db.Users.Any(u => u.Id == userId && u.DeletedAt == null && u.IsActive))
            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.ExpiresAt, expiresAt), ct) > 0;
    }

    public Task ExtendAsync(Guid sessionId, DateTimeOffset expiresAt, CancellationToken ct = default) =>
        db.UserSessions
            .Where(s => s.Id == sessionId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.ExpiresAt, expiresAt), ct);

    public async Task<bool> CloseAsync(Guid sessionId, CancellationToken ct = default) =>
        await db.UserSessions.Where(s => s.Id == sessionId).ExecuteDeleteAsync(ct) > 0;
}

/// <summary>EF Core adapter for the INTEGRATION refresh-token store.</summary>
public sealed class IntegrationRefreshTokenStore(HsmDbContext db) : IIntegrationRefreshTokenStore
{
    public Task<IntegrationRefreshToken?> FindActiveAsync(Guid integrationAccountId, CancellationToken ct = default) =>
        db.IntegrationRefreshTokens
            .FirstOrDefaultAsync(t => t.IntegrationAccountId == integrationAccountId && t.IsActive, ct);

    public async Task<Guid?> FindActiveAccountByHashAsync(string tokenHash, CancellationToken ct = default) =>
        await db.IntegrationRefreshTokens
            .AsNoTracking()
            .Where(t => t.TokenHash == tokenHash && t.IsActive)
            .Select(t => (Guid?)t.IntegrationAccountId)
            .FirstOrDefaultAsync(ct);

    public async Task<Guid?> FindAccountByHashAsync(string tokenHash, CancellationToken ct = default) =>
        await db.IntegrationRefreshTokens
            .AsNoTracking()
            .Where(t => t.TokenHash == tokenHash)
            .Select(t => (Guid?)t.IntegrationAccountId)
            .FirstOrDefaultAsync(ct);

    public Task<IntegrationRefreshToken?> FindMostRecentlySpentAsync(
        Guid integrationAccountId, CancellationToken ct = default) =>
        db.IntegrationRefreshTokens
            .AsNoTracking()
            .Where(t => t.IntegrationAccountId == integrationAccountId && !t.IsActive)
            .OrderByDescending(t => t.UpdatedAt)
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// A CONDITIONAL update, not a read followed by a write, and the whole race
    /// guard of refresh rotation rests on that. Two transactions issuing this
    /// statement for the same digest contend on one row: the second blocks until
    /// the first commits, then re-evaluates <c>is_active</c> against the
    /// committed version and matches nothing. It reports 0 and its caller
    /// refuses.
    /// </summary>
    public Task<int> ClaimActiveAsync(string tokenHash, CancellationToken ct = default) =>
        db.IntegrationRefreshTokens
            .Where(t => t.TokenHash == tokenHash && t.IsActive)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(t => t.IsActive, false)
                    .SetProperty(t => t.UpdatedAt, DateTimeOffset.UtcNow),
                ct);

    /// <summary>
    /// Account-scoped revocation, and it takes TWO statements to be honest
    /// about its own result.
    ///
    /// <para>Under READ COMMITTED a statement that blocks on a row lock wakes up
    /// still holding its original snapshot. So if a rotation is committing while
    /// this update waits, the update re-checks the row it can see, finds it
    /// inactive, and reports 0 — without ever seeing the successor row the
    /// rotation inserted. The caller then believes the account has no live
    /// credential at the exact moment it acquired a new one. A SECOND statement
    /// gets a fresh snapshot and finds it.</para>
    ///
    /// <para>One retry, not a loop: the successor is committed and visible by
    /// the time the first statement returns, so the second cannot miss it for
    /// the same reason. A further rotation would have to start after this call
    /// began and commit before the retry — at which point it is a new request
    /// arriving after a completed revocation, not a lost race.</para>
    /// </summary>
    public async Task<int> DeactivateActiveAsync(Guid integrationAccountId, CancellationToken ct = default)
    {
        var affected = await DeactivateOnceAsync(integrationAccountId, ct);
        return affected > 0 ? affected : await DeactivateOnceAsync(integrationAccountId, ct);
    }

    private Task<int> DeactivateOnceAsync(Guid integrationAccountId, CancellationToken ct) =>
        db.IntegrationRefreshTokens
            .Where(t => t.IntegrationAccountId == integrationAccountId && t.IsActive)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(t => t.IsActive, false)
                    .SetProperty(t => t.UpdatedAt, DateTimeOffset.UtcNow),
                ct);

    public async Task AddAsync(Guid integrationAccountId, string tokenHash, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        await db.IntegrationRefreshTokens.AddAsync(
            new IntegrationRefreshToken
            {
                Id = Guid.NewGuid(),
                IntegrationAccountId = integrationAccountId,
                TokenHash = tokenHash,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now,
            },
            ct);
    }
}

/// <summary>EF Core adapter for <see cref="IIntegrationAccountStore"/>.</summary>
public sealed class IntegrationAccountStore(HsmDbContext db) : IIntegrationAccountStore
{
    public async Task AddAsync(IntegrationAccount account, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        account.CreatedAt = now;
        account.UpdatedAt = now;
        await db.IntegrationAccounts.AddAsync(account, ct);
    }

    public Task<IntegrationAccount?> FindByIdAsync(Guid id, CancellationToken ct = default) =>
        db.IntegrationAccounts.FirstOrDefaultAsync(a => a.Id == id && a.DeletedAt == null, ct);

    public async Task<IReadOnlyList<IntegrationAccount>> ListAsync(CancellationToken ct = default) =>
        await db.IntegrationAccounts
            .Where(a => a.DeletedAt == null)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(ct);
}
