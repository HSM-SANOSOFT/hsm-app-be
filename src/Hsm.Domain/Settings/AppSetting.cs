namespace Hsm.Domain.Settings;

/// <summary>
/// A store-managed application setting. Mirrors the frozen app_setting table:
/// one row per key, category from the settings catalog, secrets flagged so
/// the API masks them on read and in the audit log.
/// </summary>
public class AppSetting
{
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? Value { get; set; }
    public bool IsSecret { get; set; }

    /// <summary>Id of the user who last wrote the row, or null.</summary>
    public string? UpdatedBy { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// One audit entry per effective settings change (frozen app_setting_audit):
/// who changed what, from which value to which value. Secret values are
/// stored MASKED — the plaintext never reaches this table.
/// </summary>
public class AppSettingAudit
{
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string? Category { get; set; }

    /// <summary>Id of the acting user, or null.</summary>
    public string? ChangedBy { get; set; }

    /// <summary>Previous effective value (masked for secrets; null when unset).</summary>
    public string? OldValue { get; set; }

    /// <summary>New value (masked for secrets).</summary>
    public string? NewValue { get; set; }

    public DateTimeOffset ChangedAt { get; set; }
}
