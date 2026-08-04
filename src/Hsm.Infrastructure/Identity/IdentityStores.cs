using Hsm.Application.Auth;
using Hsm.Contracts;
using Hsm.Domain.Identity;
using Hsm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Hsm.Infrastructure.Identity;

/// <summary>EF Core adapter for <see cref="IUserStore"/>.</summary>
public sealed class UserStore(HsmDbContext db) : IUserStore
{
    public Task<User?> FindByUsernameAsync(string username, CancellationToken ct = default) =>
        db.Users
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Username == username && u.DeletedAt == null, ct);

    public Task<User?> FindByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Users
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Id == id && u.DeletedAt == null, ct);

    public Task<User?> FindActiveByEmailAsync(string email, CancellationToken ct = default) =>
        db.Users
            .FirstOrDefaultAsync(u => u.Email == email && u.IsActive && u.DeletedAt == null, ct);

    public async Task<(bool Found, DateTimeOffset? OnboardingCompletedAt)> OnboardingStateAsync(
        Guid id, CancellationToken ct = default)
    {
        var row = await db.Users.AsNoTracking()
            .Where(u => u.Id == id && u.DeletedAt == null)
            .Select(u => new { u.OnboardingCompletedAt })
            .FirstOrDefaultAsync(ct);
        return row is null ? (false, null) : (true, row.OnboardingCompletedAt);
    }

    public async Task AddAsync(User user, IEnumerable<string> roles, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        user.CreatedAt = now;
        user.UpdatedAt = now;
        foreach (var role in roles)
        {
            // Unknown roles resolve to no domain and are skipped — the frozen
            // RolesService.findRoleDomains behavior.
            var domain = RoleCatalog.DomainOf(role);
            if (domain is null)
            {
                continue;
            }

            user.Roles.Add(new UserRole
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Domain = domain,
                Role = role,
                CreatedAt = now,
            });
        }

        await db.Users.AddAsync(user, ct);
    }

    public async Task UpdatePasswordAsync(Guid userId, string passwordHash, CancellationToken ct = default)
    {
        await db.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(u => u.PasswordHash, passwordHash)
                    .SetProperty(u => u.UpdatedAt, DateTimeOffset.UtcNow),
                ct);
    }

    public async Task<PagedResult<User>> ListAsync(
        int page, int pageSize, CancellationToken ct = default)
    {
        var live = db.Users.Where(u => u.DeletedAt == null);
        var totalItems = await live.CountAsync(ct);
        var users = await live
            .AsNoTracking()
            .Include(u => u.Roles)
            .OrderByDescending(u => u.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return new PagedResult<User>(users, page, pageSize, totalItems);
    }

    public async Task<IReadOnlyList<UserRole>> ReplaceRolesAsync(
        Guid userId, IEnumerable<string> roles, CancellationToken ct = default)
    {
        // Immediate delete inside the caller's transaction, THEN staged
        // inserts — the frozen statement order, so re-assigning a held role
        // never collides with the (user, domain, role) unique index.
        await db.UserRoles.Where(r => r.UserId == userId).ExecuteDeleteAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var added = new List<UserRole>();
        foreach (var role in roles)
        {
            var domain = RoleCatalog.DomainOf(role);
            if (domain is null)
            {
                continue;
            }

            var row = new UserRole
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Domain = domain,
                Role = role,
                CreatedAt = now,
            };
            db.UserRoles.Add(row);
            added.Add(row);
        }

        return added;
    }
}

/// <summary>EF Core adapter for the USER refresh-token store.</summary>
public sealed class UserRefreshTokenStore(HsmDbContext db) : IUserRefreshTokenStore
{
    public Task<UserRefreshToken?> FindActiveAsync(Guid userId, CancellationToken ct = default) =>
        db.UserRefreshTokens.FirstOrDefaultAsync(t => t.UserId == userId && t.IsActive, ct);

    public Task<int> DeactivateActiveAsync(Guid userId, CancellationToken ct = default) =>
        db.UserRefreshTokens
            .Where(t => t.UserId == userId && t.IsActive)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(t => t.IsActive, false)
                    .SetProperty(t => t.UpdatedAt, DateTimeOffset.UtcNow),
                ct);

    public async Task AddAsync(Guid userId, string tokenHash, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        await db.UserRefreshTokens.AddAsync(
            new UserRefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TokenHash = tokenHash,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now,
            },
            ct);
    }
}

/// <summary>EF Core adapter for the INTEGRATION refresh-token store.</summary>
public sealed class IntegrationRefreshTokenStore(HsmDbContext db) : IIntegrationRefreshTokenStore
{
    public Task<IntegrationRefreshToken?> FindActiveAsync(Guid integrationAccountId, CancellationToken ct = default) =>
        db.IntegrationRefreshTokens
            .FirstOrDefaultAsync(t => t.IntegrationAccountId == integrationAccountId && t.IsActive, ct);

    public Task<int> DeactivateActiveAsync(Guid integrationAccountId, CancellationToken ct = default) =>
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

/// <summary>EF Core adapter for <see cref="IPasswordResetTokenStore"/>.</summary>
public sealed class PasswordResetTokenStore(HsmDbContext db) : IPasswordResetTokenStore
{
    public Task<int> CountForUserSinceAsync(Guid userId, DateTimeOffset since, CancellationToken ct = default) =>
        db.PasswordResetTokens.CountAsync(t => t.UserId == userId && t.CreatedAt > since, ct);

    public async Task AddAsync(PasswordResetToken token, CancellationToken ct = default)
    {
        token.CreatedAt = DateTimeOffset.UtcNow;
        await db.PasswordResetTokens.AddAsync(token, ct);
    }

    public Task<PasswordResetToken?> FindByHashAsync(string tokenHash, CancellationToken ct = default) =>
        db.PasswordResetTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    public async Task<bool> TryConsumeAsync(Guid tokenId, CancellationToken ct = default)
    {
        // Conditional UPDATE — the atomic single-use guarantee: of two racing
        // consumers, exactly one affects a row.
        var affected = await db.PasswordResetTokens
            .Where(t => t.Id == tokenId && t.UsedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(t => t.UsedAt, DateTimeOffset.UtcNow),
                ct);
        return affected > 0;
    }

    public Task DeleteByHashAsync(string tokenHash, CancellationToken ct = default) =>
        db.PasswordResetTokens.Where(t => t.TokenHash == tokenHash).ExecuteDeleteAsync(ct);
}

/// <summary>
/// Commits the shared DbContext; transactions span all stores in scope.
///
/// <para><b>Joins an ambient transaction rather than nesting.</b> When this
/// DbContext already has an open transaction — which it does for every ICommand,
/// because TransactionBehavior opens one — the work runs inside that transaction
/// and commits or rolls back with it, instead of asking EF for a nested
/// transaction it refuses outright. That is what lets shared collaborators below
/// the pipeline keep their own <c>ExecuteInTransactionAsync</c> calls, TokenIssuer's
/// refresh rotation first among them. A caller that needs an INDEPENDENT
/// transaction (an audit row, an outbox write, or a compensating delete that must
/// survive an outer rollback) must NOT use this type: it will be silently
/// absorbed into the outer unit. The join is logged at Debug so the case is
/// discoverable instead of invisible.</para>
/// </summary>
public sealed partial class AuthUnitOfWork(HsmDbContext db, ILogger<AuthUnitOfWork> logger) : IAuthUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);

    public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct = default)
    {
        var ambient = db.Database.CurrentTransaction;
        if (ambient is not null)
        {
            LogJoinedAmbientTransaction(logger, ambient.TransactionId);
            return await work(ct);
        }

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var result = await work(ct);
            await transaction.CommitAsync(ct);
            return result;
        });
    }

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "AuthUnitOfWork.ExecuteInTransactionAsync joined the ambient transaction {TransactionId} "
            + "instead of opening its own; this work commits or rolls back with the outer unit")]
    private static partial void LogJoinedAmbientTransaction(ILogger logger, Guid transactionId);
}
