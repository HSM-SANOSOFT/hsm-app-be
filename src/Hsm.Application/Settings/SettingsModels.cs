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

/// <summary>
/// One setting as exposed by the API (frozen SettingItemDto). <see cref="UpdatedAt"/>
/// is the stored row's <c>AppSetting.UpdatedAt</c> when a row exists, or null
/// for a catalog entry that has never been written (still on its
/// deploy-environment seed, if any) —
/// <see cref="Queries.GetSettings.GetSettingsHandler"/> populates it from the
/// same row lookup it already does for <see cref="Value"/>.
/// </summary>
public sealed record SettingItem(
    string Key, string Category, bool IsSecret, bool IsSet, string? Value, DateTimeOffset? UpdatedAt);

/// <summary>A category read-back (frozen GetSettingsResponseDto).</summary>
public sealed record SettingsView(string Category, IReadOnlyList<SettingItem> Settings);

/// <summary>One requested change (frozen UpdateSettingItemDto).</summary>
public sealed record SettingUpdate(string Key, string? Value);
