namespace Hsm.Contracts.Ui;

/// <summary>
/// The signed-in identity as the shell needs it: who to greet in the top bar
/// and which navigation items to render. Declared here (the boundary's only
/// shared surface) and implemented by the host from its validated session —
/// components never see the application-layer principal.
/// </summary>
public interface ICurrentUserUiService
{
    /// <summary>The current user, or null when the session is anonymous.</summary>
    Task<CurrentUserDto?> GetCurrentUserAsync(CancellationToken cancellationToken = default);
}

/// <summary>What the shell may know about the signed-in user.</summary>
public sealed record CurrentUserDto(string Id, string DisplayName, IReadOnlyList<string> Roles)
{
    public bool IsInRole(string role) => Roles.Contains(role);
}

/// <summary>
/// Role identifiers the UI branches on. The string values are contract (they
/// ride in JWTs); the component library cannot reference the domain catalog,
/// so the few roles the shell needs are re-declared on this side of the
/// boundary.
/// </summary>
public static class UiRoles
{
    public const string Admin = "admin";
}
