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
/// place that decision is made.
///
/// <para><see cref="Description"/> and <see cref="UpdatedAt"/> are always
/// null: the query result this projects from, <see cref="SettingItem"/>,
/// carries neither — the catalog's <see cref="SettingDefinition"/> has no
/// description text, and <c>GetSettingsHandler</c> folds the stored row into
/// a masked value without keeping its <c>AppSetting.UpdatedAt</c>. Populating
/// them for real means widening that Application-layer query result, which is
/// out of this endpoint-reshape task's scope; they stay in the wire contract
/// because the brief specifies them, ready for a future task to source.</para>
/// </summary>
public sealed record SettingItemResource(
    string Key, string? Value, bool IsSecret, string? Description, DateTimeOffset? UpdatedAt)
{
    public static SettingItemResource From(SettingItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new SettingItemResource(item.Key, item.Value, item.IsSecret, Description: null, UpdatedAt: null);
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
/// <para><see cref="ChangedBy"/> is <see cref="Guid"/>? rather than the
/// brief's plain <see cref="Guid"/>: <see cref="AppSettingAudit.ChangedBy"/>
/// is stored as <c>string?</c> (it carries <c>RequestActor.Id</c>, a bare
/// string everywhere else in the pipeline — see
/// <see cref="Hsm.Application.Abstractions.RequestActor"/>), so a non-nullable
/// <see cref="Guid"/> here would mean an unguarded parse that turns a
/// corrupt/legacy row into a 500 on an otherwise-successful read. Every row
/// this endpoint can currently produce comes from
/// <c>UpdateSettingsHandler</c>, which always supplies a real actor id before
/// it writes an audit row, so this parses successfully in practice — the
/// nullability is defensive, not a sign any row is expected to lack it.</para>
/// </summary>
public sealed record SettingAuditResource(
    Guid Id, string Category, string Key, string? OldValue, string? NewValue, Guid? ChangedBy, DateTimeOffset ChangedAt)
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
            Guid.TryParse(audit.ChangedBy, out var changedBy) ? changedBy : null,
            audit.ChangedAt);
    }
}
