using Hsm.Application.Settings;
using Hsm.Contracts.Ui;

namespace Hsm.Web.Services;

/// <summary>
/// Host-side settings administration (plan U18, screen 4): admin gate first,
/// then the same handlers the frozen /v1/settings endpoints call — masking
/// and the transactional write+audit guarantee live in the handlers, not
/// here. The authenticated admin is the audit actor.
/// </summary>
public sealed class SettingsAdminUiService(
    UiServiceGate gate,
    GetSettingsHandler getSettings,
    UpdateSettingsHandler updateSettings,
    ListSettingsAuditHandler listAudit) : ISettingsAdminUiService
{
    public async Task<SettingsCategoryDto> GetSettingsAsync(
        string category, CancellationToken cancellationToken = default)
    {
        await gate.RequireAdminAsync();
        RequireKnownCategory(category);
        return ToDto(await getSettings.HandleAsync(category, cancellationToken));
    }

    public async Task<SettingsCategoryDto> UpdateSettingsAsync(
        string category, IReadOnlyList<SettingChangeDto> changes, CancellationToken cancellationToken = default)
    {
        var adminId = await gate.RequireAdminIdAsync();
        RequireKnownCategory(category);
        var view = await updateSettings.HandleAsync(
            category,
            [.. changes.Select(change => new SettingUpdate(change.Key, change.Value))],
            adminId.ToString(),
            cancellationToken);
        return ToDto(view);
    }

    public async Task<IReadOnlyList<SettingAuditEntryDto>> GetAuditTrailAsync(
        string category, CancellationToken cancellationToken = default)
    {
        await gate.RequireAdminAsync();
        RequireKnownCategory(category);
        var entries = await listAudit.HandleAsync(category, ct: cancellationToken);
        return [.. entries.Select(entry => new SettingAuditEntryDto(
            entry.Key, entry.ChangedBy, entry.OldValue, entry.NewValue, entry.ChangedAt))];
    }

    private static void RequireKnownCategory(string category)
    {
        if (!UiSettingCategories.All.Contains(category, StringComparer.Ordinal))
        {
            throw new ArgumentException($"Categoría desconocida: '{category}'.", nameof(category));
        }
    }

    private static SettingsCategoryDto ToDto(SettingsView view) =>
        new(view.Category, [.. view.Settings.Select(item =>
            new SettingItemDto(item.Key, item.Category, item.IsSecret, item.IsSet, item.Value))]);
}
