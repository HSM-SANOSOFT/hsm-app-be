using Hsm.Application.Settings;
using Hsm.Contracts;
using Hsm.Domain.Settings;
using Hsm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Hsm.Infrastructure.Settings;

/// <summary>EF Core adapter for <see cref="IAppSettingStore"/>.</summary>
public sealed class AppSettingStore(HsmDbContext db) : IAppSettingStore
{
    public async Task<IReadOnlyList<AppSetting>> FindByKeysAsync(
        IReadOnlyCollection<string> keys, CancellationToken ct = default) =>
        keys.Count == 0
            ? []
            : await db.AppSettings.Where(s => keys.Contains(s.Key)).ToListAsync(ct);

    public async Task AddAsync(AppSetting setting, CancellationToken ct = default) =>
        await db.AppSettings.AddAsync(setting, ct);

    public async Task AddAuditAsync(AppSettingAudit audit, CancellationToken ct = default) =>
        await db.AppSettingAudits.AddAsync(audit, ct);

    public async Task<PagedResult<AppSettingAudit>> ListAuditAsync(
        string category, int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.AppSettingAudits.Where(a => a.Category == category);
        var totalItems = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(a => a.ChangedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return new PagedResult<AppSettingAudit>(items, page, pageSize, totalItems);
    }
}

/// <summary>
/// Deploy-environment seeds bound from configuration
/// (Settings:Seed:&lt;KEY&gt;) — the frozen definitions read process env vars
/// of the same key names; configuration is this stack's equivalent surface.
/// </summary>
public sealed class ConfigurationSettingSeedSource(IConfiguration configuration) : ISettingSeedSource
{
    public string? SeedValueFor(string key) => configuration[$"Settings:Seed:{key}"];
}
