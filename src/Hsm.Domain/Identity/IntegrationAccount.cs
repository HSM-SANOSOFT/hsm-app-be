namespace Hsm.Domain.Identity;

/// <summary>
/// A machine (integration) consumer — a separate principal type from
/// <see cref="User"/> with its own token store.
/// </summary>
public class IntegrationAccount
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>One of <see cref="IntegrationFunctionality"/>: prod | staging | dev.</summary>
    public string Functionality { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

/// <summary>Integration account functionality tier values.</summary>
public static class IntegrationFunctionality
{
    public const string Prod = "prod";
    public const string Staging = "staging";
    public const string Dev = "dev";

    public static readonly IReadOnlyList<string> All = [Prod, Staging, Dev];
}
