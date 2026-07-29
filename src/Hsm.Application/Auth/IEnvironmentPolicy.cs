namespace Hsm.Application.Auth;

/// <summary>
/// Deployment environment gate for the developer role (frozen envs.ENVIRONMENT):
/// developer tokens/access are only permitted in dev.
/// </summary>
public interface IEnvironmentPolicy
{
    bool IsDev { get; }
}
