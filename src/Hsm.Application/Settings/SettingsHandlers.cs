using Hsm.Application.Auth;
using Hsm.Domain.Settings;

namespace Hsm.Application.Settings;

/// <summary>
/// Placeholder returned for any secret-valued setting on read, and stored in
/// the audit log in place of the real plaintext (frozen SECRET_MASK). The
/// real secret value is NEVER sent back to a client nor written to the audit
/// table.
/// </summary>
public static class SettingsPolicy
{
    public const string SecretMask = "********";
}

/// <summary>One setting as exposed by the API (frozen SettingItemDto).</summary>
public sealed record SettingItem(string Key, string Category, bool IsSecret, bool IsSet, string? Value);

/// <summary>A category read-back (frozen GetSettingsResponseDto).</summary>
public sealed record SettingsView(string Category, IReadOnlyList<SettingItem> Settings);

/// <summary>One requested change (frozen UpdateSettingItemDto).</summary>
public sealed record SettingUpdate(string Key, string? Value);

/// <summary>
/// Category read (frozen getByCategory): catalog-driven — every definition of
/// the category is returned, the effective value being the stored row or the
/// deploy-environment seed; secrets are masked (value null when unset).
/// </summary>
public sealed class GetSettingsHandler(IAppSettingStore store, ISettingSeedSource seeds)
{
    public async Task<SettingsView> HandleAsync(string category, CancellationToken ct = default)
    {
        var definitions = SettingCatalog.ForCategory(category);
        var rows = await store.FindByKeysAsync([.. definitions.Select(d => d.Key)], ct);
        var rowByKey = rows.ToDictionary(r => r.Key, StringComparer.Ordinal);

        var items = definitions
            .Select(def =>
            {
                var raw = rowByKey.TryGetValue(def.Key, out var row) ? row.Value : seeds.SeedValueFor(def.Key);
                var isSet = !string.IsNullOrEmpty(raw);
                return new SettingItem(
                    def.Key,
                    def.Category,
                    def.IsSecret,
                    isSet,
                    def.IsSecret ? (isSet ? SettingsPolicy.SecretMask : null) : raw);
            })
            .ToList();

        return new SettingsView(category, items);
    }
}

/// <summary>
/// Settings update (frozen SettingsService.update): every row write AND its
/// audit entry commit in ONE transaction (R11) — settings can never change
/// without a matching audit trail. Unknown or category-mismatched keys are
/// ignored, a blank value never overwrites a secret, and no-op writes produce
/// no audit entry. Audit rows record the actor and the old/new values, with
/// secrets masked on both sides.
/// </summary>
public sealed class UpdateSettingsHandler(
    IAppSettingStore store,
    ISettingSeedSource seeds,
    IAuthUnitOfWork unitOfWork,
    GetSettingsHandler reader)
{
    public async Task<SettingsView> HandleAsync(
        string category,
        IReadOnlyList<SettingUpdate> updates,
        string? actorId,
        CancellationToken ct = default)
    {
        var rows = await store.FindByKeysAsync([.. updates.Select(u => u.Key).Distinct(StringComparer.Ordinal)], ct);
        var rowByKey = rows.ToDictionary(r => r.Key, StringComparer.Ordinal);

        await unitOfWork.ExecuteInTransactionAsync(
            async innerCt =>
            {
                foreach (var update in updates)
                {
                    var def = SettingCatalog.ForKey(update.Key);
                    if (def is null || def.Category != category)
                    {
                        // Never create arbitrary rows.
                        continue;
                    }

                    var incoming = update.Value ?? string.Empty;
                    if (def.IsSecret && string.IsNullOrWhiteSpace(incoming))
                    {
                        // A blank secret leaves the stored value unchanged.
                        continue;
                    }

                    var previous = rowByKey.TryGetValue(def.Key, out var row)
                        ? row.Value
                        : seeds.SeedValueFor(def.Key);
                    if (string.Equals(previous, incoming, StringComparison.Ordinal))
                    {
                        // Effective value unchanged: no write, no audit.
                        continue;
                    }

                    var now = DateTimeOffset.UtcNow;
                    if (row is not null)
                    {
                        row.Value = incoming;
                        row.Category = def.Category;
                        row.IsSecret = def.IsSecret;
                        row.UpdatedBy = actorId;
                        row.UpdatedAt = now;
                    }
                    else
                    {
                        await store.AddAsync(
                            new AppSetting
                            {
                                Id = Guid.NewGuid(),
                                Key = def.Key,
                                Category = def.Category,
                                IsSecret = def.IsSecret,
                                Value = incoming,
                                UpdatedBy = actorId,
                                UpdatedAt = now,
                            },
                            innerCt);
                    }

                    await store.AddAuditAsync(
                        new AppSettingAudit
                        {
                            Id = Guid.NewGuid(),
                            Key = def.Key,
                            Category = def.Category,
                            ChangedBy = actorId,
                            OldValue = def.IsSecret
                                ? (string.IsNullOrEmpty(previous) ? null : SettingsPolicy.SecretMask)
                                : previous,
                            NewValue = def.IsSecret ? SettingsPolicy.SecretMask : incoming,
                            ChangedAt = now,
                        },
                        innerCt);
                }

                await unitOfWork.SaveChangesAsync(innerCt);
                return true;
            },
            ct);

        // Frozen behavior: respond with the fresh category read-back.
        return await reader.HandleAsync(category, ct);
    }
}
