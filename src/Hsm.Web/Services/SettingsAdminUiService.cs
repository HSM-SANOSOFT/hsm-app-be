using Hsm.Application.Abstractions;
using Hsm.Application.Settings;
using Hsm.Application.Settings.Commands.UpdateSettings;
using Hsm.Application.Settings.Queries.GetSettings;
using Hsm.Application.Settings.Queries.ListSettingsAudit;
using Hsm.Contracts.Ui;

namespace Hsm.Web.Services;

/// <summary>
/// Host-side settings administration (plan U18, screen 4): admin gate first,
/// then the same command/query slices the frozen /v1/settings endpoints
/// dispatch — masking and the transactional write+audit guarantee live in the
/// handlers, not here. The authenticated admin is the audit actor, read from
/// ICurrentPrincipal (installed by the gate) rather than passed as a parameter.
/// </summary>
public sealed class SettingsAdminUiService(UiServiceGate gate, IDispatcher dispatcher) : ISettingsAdminUiService
{
    public async Task<SettingsCategoryDto> GetSettingsAsync(
        string category, CancellationToken cancellationToken = default)
    {
        await gate.RequireAdminAsync();
        RequireKnownCategory(category);
        return ToDto(await dispatcher.Send(new GetSettingsQuery(category), cancellationToken));
    }

    public async Task<SettingsCategoryDto> UpdateSettingsAsync(
        string category, IReadOnlyList<SettingChangeDto> changes, CancellationToken cancellationToken = default)
    {
        await gate.RequireAdminAsync();
        RequireKnownCategory(category);
        var view = await dispatcher.Send(
            new UpdateSettingsCommand(
                category, [.. changes.Select(change => new SettingUpdate(change.Key, change.Value))]),
            cancellationToken);
        return ToDto(view);
    }

    public async Task<IReadOnlyList<SettingAuditEntryDto>> GetAuditTrailAsync(
        string category, CancellationToken cancellationToken = default)
    {
        await gate.RequireAdminAsync();
        RequireKnownCategory(category);
        var entries = await dispatcher.Send(new ListSettingsAuditQuery(category), cancellationToken);
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
