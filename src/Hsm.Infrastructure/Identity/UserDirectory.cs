using Hsm.Application.Auth;
using Hsm.Contracts;
using Hsm.Domain.Identity;
using Hsm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Hsm.Infrastructure.Identity;

/// <summary>
/// EF Core adapter for <see cref="IUserDirectory"/> — the three reads
/// UserManager either cannot do at all or cannot do in one round trip.
/// </summary>
public sealed class UserDirectory(HsmDbContext db) : IUserDirectory
{
    public async Task<(bool Found, DateTimeOffset? OnboardingCompletedAt)> OnboardingStateAsync(
        Guid id, CancellationToken ct = default)
    {
        var row = await db.Users.AsNoTracking()
            .Where(u => u.Id == id && u.DeletedAt == null && u.IsActive)
            .Select(u => new { u.OnboardingCompletedAt })
            .FirstOrDefaultAsync(ct);
        return row is null ? (false, null) : (true, row.OnboardingCompletedAt);
    }

    public async Task<PagedResult<HsmUser>> ListAsync(
        int page, int pageSize, CancellationToken ct = default)
    {
        var live = db.Users.Where(u => u.DeletedAt == null);
        var totalItems = await live.CountAsync(ct);
        var users = await live
            .AsNoTracking()
            .OrderByDescending(u => u.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return new PagedResult<HsmUser>(users, page, pageSize, totalItems);
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> RolesForAsync(
        IReadOnlyList<Guid> userIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        if (userIds.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<string>>();
        }

        // One join over the assignment table and the role table, for the whole
        // page. UserManager.GetRolesAsync is per-user; a 20-row page through it
        // would be 21 queries.
        var pairs = await (
            from assignment in db.UserRoles
            join role in db.Roles on assignment.RoleId equals role.Id
            where userIds.Contains(assignment.UserId)
            select new { assignment.UserId, role.Name })
            .AsNoTracking()
            .ToListAsync(ct);

        return pairs
            .GroupBy(p => p.UserId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)[.. group.Select(p => p.Name!).OrderBy(n => n, StringComparer.Ordinal)]);
    }
}
