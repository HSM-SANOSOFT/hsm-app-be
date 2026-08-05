namespace Hsm.Application.Settings;

/// <summary>
/// Deploy-environment seed values for catalog keys: when
/// no database row exists, the effective value of a setting falls back to the
/// deploy environment. Null when the environment does not provide one.
/// </summary>
public interface ISettingSeedSource
{
    string? SeedValueFor(string key);
}
