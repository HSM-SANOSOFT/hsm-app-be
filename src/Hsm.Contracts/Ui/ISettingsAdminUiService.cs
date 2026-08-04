namespace Hsm.Contracts.Ui;

/// <summary>
/// Settings administration (plan U18, screen 4): catalog-driven view and
/// edit per category, secrets masked on read, and the audit trail (actor,
/// old value, new value) the frozen system writes with every effective
/// change. The host implementation calls the settings handlers in process
/// with the authenticated admin as the audit actor.
/// </summary>
public interface ISettingsAdminUiService
{
    Task<SettingsCategoryDto> GetSettingsAsync(string category, CancellationToken cancellationToken = default);

    /// <summary>Applies the changes and returns the fresh category read-back.</summary>
    Task<SettingsCategoryDto> UpdateSettingsAsync(
        string category, IReadOnlyList<SettingChangeDto> changes, CancellationToken cancellationToken = default);

    /// <summary>Audit entries for the category, newest first, paged.</summary>
    Task<PagedResult<SettingAuditEntryDto>> GetAuditTrailAsync(
        string category, int page, int pageSize, CancellationToken cancellationToken = default);
}

/// <summary>A category read-back as the screen renders it.</summary>
public sealed record SettingsCategoryDto(string Category, IReadOnlyList<SettingItemDto> Settings);

/// <summary>One setting: secrets arrive masked (or null when unset), never plaintext.</summary>
public sealed record SettingItemDto(string Key, string Category, bool IsSecret, bool IsSet, string? Value);

/// <summary>One requested change.</summary>
public sealed record SettingChangeDto(string Key, string? Value);

/// <summary>One audit entry: who changed what, from which value to which (secrets masked).</summary>
public sealed record SettingAuditEntryDto(
    string Key,
    string? ChangedBy,
    string? OldValue,
    string? NewValue,
    DateTimeOffset ChangedAt);

/// <summary>
/// Frozen settings categories, re-declared on this side of the boundary (the
/// string values are contract; the component library cannot reference the
/// domain catalog).
/// </summary>
public static class UiSettingCategories
{
    public const string Email = "EMAIL";
    public const string Webhook = "WEBHOOK";
    public const string Storage = "STORAGE";
    public const string AppBehavior = "APP_BEHAVIOR";

    public static readonly IReadOnlyList<string> All = [Email, Webhook, Storage, AppBehavior];
}
