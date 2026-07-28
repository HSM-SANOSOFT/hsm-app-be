namespace Hsm.Domain.Settings;

/// <summary>
/// Setting categories frozen by the reference implementation
/// (packages/common/src/enums/settings.enum.ts). Values are contract.
/// </summary>
public static class SettingsCategories
{
    public const string Email = "EMAIL";
    public const string Webhook = "WEBHOOK";
    public const string Storage = "STORAGE";
    public const string AppBehavior = "APP_BEHAVIOR";

    public static readonly IReadOnlyList<string> All = [Email, Webhook, Storage, AppBehavior];
}

/// <summary>A catalog entry: key, category, and whether the value is secret.</summary>
public sealed record SettingDefinition(string Key, string Category, bool IsSecret);

/// <summary>
/// The store-managed settings catalog, frozen at
/// packages/database/src/settings/setting-definitions.ts. Only these keys are
/// readable/writable through the settings API — unknown keys are ignored, and
/// infra keys (DB/Redis/JWT, throttler limits) are deliberately absent: they
/// stay deploy-only. Each definition's deploy-environment seed is resolved
/// through the ISettingSeedSource port, mirroring the frozen envValue().
/// </summary>
public static class SettingCatalog
{
    public static readonly IReadOnlyList<SettingDefinition> Definitions =
    [
        // EMAIL / SMTP
        new("SMTP_ADDRESS", SettingsCategories.Email, IsSecret: false),
        new("SMTP_USERNAME", SettingsCategories.Email, IsSecret: false),
        new("SMTP_PASSWORD", SettingsCategories.Email, IsSecret: true),
        new("SMTP_PORT", SettingsCategories.Email, IsSecret: false),
        new("SMTP_SECURE", SettingsCategories.Email, IsSecret: false),
        // WEBHOOK signing keys
        new("COMS_WEBHOOK_SIGNING_KEYS", SettingsCategories.Webhook, IsSecret: true),
        // STORAGE / S3 (access key non-secret, secret key secret — frozen flags)
        new("STRG_S3_ACCESS_KEY", SettingsCategories.Storage, IsSecret: false),
        new("STRG_S3_SECRET_KEY", SettingsCategories.Storage, IsSecret: true),
        new("STRG_S3_HOST", SettingsCategories.Storage, IsSecret: false),
        new("STRG_S3_HOST_EXTERNAL", SettingsCategories.Storage, IsSecret: false),
        new("STRG_S3_REGION", SettingsCategories.Storage, IsSecret: false),
        new("STRG_S3_FORCE_PATH_STYLE", SettingsCategories.Storage, IsSecret: false),
        // APP_BEHAVIOR toggles
        new("SWAGGER_SITE_TITLE", SettingsCategories.AppBehavior, IsSecret: false),
    ];

    public static IReadOnlyList<SettingDefinition> ForCategory(string category) =>
        [.. Definitions.Where(d => d.Category == category)];

    public static SettingDefinition? ForKey(string key) =>
        Definitions.FirstOrDefault(d => d.Key == key);
}
