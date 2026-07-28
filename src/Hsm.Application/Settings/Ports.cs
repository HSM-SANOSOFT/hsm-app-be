using Hsm.Domain.Settings;

namespace Hsm.Application.Settings;

/// <summary>Persistence port for application settings and their audit trail.</summary>
public interface IAppSettingStore
{
    /// <summary>Tracked rows for the given keys (absent keys yield no row).</summary>
    Task<IReadOnlyList<AppSetting>> FindByKeysAsync(IReadOnlyCollection<string> keys, CancellationToken ct = default);

    /// <summary>Stages a new setting row.</summary>
    Task AddAsync(AppSetting setting, CancellationToken ct = default);

    /// <summary>Stages an audit entry.</summary>
    Task AddAuditAsync(AppSettingAudit audit, CancellationToken ct = default);

    /// <summary>Audit entries for a category, newest first, capped at <paramref name="limit"/>.</summary>
    Task<IReadOnlyList<AppSettingAudit>> ListAuditAsync(
        string category, int limit, CancellationToken ct = default);
}

/// <summary>
/// Deploy-environment seed values for catalog keys (frozen envValue()): when
/// no database row exists, the effective value of a setting falls back to the
/// deploy environment. Null when the environment does not provide one.
/// </summary>
public interface ISettingSeedSource
{
    string? SeedValueFor(string key);
}
