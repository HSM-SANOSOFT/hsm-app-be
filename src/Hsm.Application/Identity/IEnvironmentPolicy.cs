namespace Hsm.Application.Identity;

/// <summary>
/// Deployment environment gate for the developer role: developer
/// tokens/access are only permitted in dev.
/// </summary>
public interface IEnvironmentPolicy
{
    bool IsDev { get; }
}
