using Hsm.Application.Settings;
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

    public async Task<IReadOnlyList<AppSettingAudit>> ListAuditAsync(
        string category, int limit, CancellationToken ct = default) =>
        await db.AppSettingAudits
            .Where(a => a.Category == category)
            .OrderByDescending(a => a.ChangedAt)
            .Take(limit)
            .ToListAsync(ct);
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
