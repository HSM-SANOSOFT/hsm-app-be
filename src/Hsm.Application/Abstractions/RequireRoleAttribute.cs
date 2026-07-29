namespace Hsm.Application.Abstractions;

/// <summary>Roles permitted to dispatch this request. Absent = authentication only.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class RequireRoleAttribute(params string[] roles) : Attribute
{
    public IReadOnlyList<string> Roles { get; } = roles;
}

/// <summary>No principal required (sign-in, password reset, provider webhooks).</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class AllowAnonymousRequestAttribute : Attribute;

/// <summary>Dispatchable by a user who has not completed onboarding.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class AllowPendingOnboardingAttribute : Attribute;
