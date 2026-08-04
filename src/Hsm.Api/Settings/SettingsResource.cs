using Hsm.Application.Settings;
using Hsm.Domain.Settings;

namespace Hsm.Api.Settings;

/// <summary>The category read-back (frozen GetSettingsResponseDto).</summary>
public sealed record SettingsResource(string Category, IReadOnlyList<SettingItemResource> Settings)
{
    public static SettingsResource From(SettingsView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return new SettingsResource(view.Category, [.. view.Settings.Select(SettingItemResource.From)]);
    }
}

/// <summary>
/// One setting as exposed by the API. <see cref="Value"/> is already masked
/// for a secret key by
/// <see cref="Hsm.Application.Settings.Queries.GetSettings.GetSettingsHandler"/>
/// (it returns <see cref="SettingsPolicy.SecretMask"/> when set, null when
/// not) — this resource does no masking of its own, so there is exactly one
/// place that decision is made. <see cref="UpdatedAt"/> is the stored row's
/// <c>AppSetting.UpdatedAt</c>, sourced the same way, null for a catalog entry
/// that has never been written.
///
/// <para>No <c>Description</c> field: the brief's draft included one, but no
/// description text exists anywhere in the domain — <see cref="SettingDefinition"/>
/// is <c>(Key, Category, IsSecret)</c> only, and authoring prose for the 13
/// catalog entries is a product decision out of this task's scope. Shipping a
/// field that can only ever be null would document a lie about what this
/// endpoint returns, so it is omitted rather than stubbed.</para>
/// </summary>
public sealed record SettingItemResource(string Key, string? Value, bool IsSecret, DateTimeOffset? UpdatedAt)
{
    public static SettingItemResource From(SettingItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new SettingItemResource(item.Key, item.Value, item.IsSecret, item.UpdatedAt);
    }
}

/// <summary>The frozen UpdateSettingsDto surface.</summary>
public sealed record UpdateSettingsRequest(string Category, IReadOnlyList<SettingUpdateRequest> Settings);

/// <summary>The frozen UpdateSettingItemDto surface.</summary>
public sealed record SettingUpdateRequest(string Key, string? Value);

/// <summary>
/// One settings-audit row (frozen <c>app_setting_audit</c> /
/// <see cref="AppSettingAudit"/>). <see cref="OldValue"/>/<see cref="NewValue"/>
/// are already masked for secret keys by
/// <see cref="Hsm.Application.Settings.Commands.UpdateSettings.UpdateSettingsHandler"/>
/// at write time — this resource does no masking of its own either.
///
/// <para><see cref="ChangedBy"/> is <c>string?</c> rather than the brief's
/// <see cref="Guid"/>: <see cref="AppSettingAudit.ChangedBy"/> is stored as
/// <c>string?</c> — the same type the existing Blazor UI contract already
/// uses for this field (<c>Hsm.Contracts.Ui.SettingAuditEntryDto.ChangedBy</c>)
/// — so this matches storage and the sibling surface exactly instead of
/// re-typing the field a third way. A parse-to-<see cref="Guid"/> was tried
/// and rejected: <c>Guid.TryParse(...) ? changedBy : null</c> would silently
/// turn a non-GUID id into <see langword="null"/>, and on an audit trail a
/// reader could no longer tell "no actor recorded" from "actor id wasn't
/// parseable" — the wrong failure mode for a field whose entire purpose is
/// attribution.</para>
/// </summary>
public sealed record SettingAuditResource(
    Guid Id, string Category, string Key, string? OldValue, string? NewValue, string? ChangedBy, DateTimeOffset ChangedAt)
{
    public static SettingAuditResource From(AppSettingAudit audit)
    {
        ArgumentNullException.ThrowIfNull(audit);
        return new SettingAuditResource(
            audit.Id,
            audit.Category ?? string.Empty,
            audit.Key,
            audit.OldValue,
            audit.NewValue,
            audit.ChangedBy,
            audit.ChangedAt);
    }
}
