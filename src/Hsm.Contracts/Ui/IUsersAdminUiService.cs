namespace Hsm.Contracts.Ui;

/// <summary>
/// Admin user management for the users-and-roles screen (plan U18, screen 2):
/// paginated listing, staff provisioning with role assignment at creation,
/// and role change. The host implementation calls the transactional
/// application handlers in process and enforces the admin gate from the
/// authenticated principal — the same gate the REST endpoints apply.
/// </summary>
public interface IUsersAdminUiService
{
    Task<PagedResult<UserRowDto>> ListUsersAsync(int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// Provisions a staff account pending first-login onboarding. The
    /// temporary password is delivered out of band (welcome email) — it is
    /// never echoed back through this surface.
    /// </summary>
    Task<UserRowDto> CreateStaffAsync(NewStaffUserDto command, CancellationToken cancellationToken = default);

    Task<UserRowDto> ChangeRoleAsync(string userId, string role, CancellationToken cancellationToken = default);

    /// <summary>Roles assignable to staff accounts, in catalog order.</summary>
    Task<IReadOnlyList<string>> GetAssignableRolesAsync(CancellationToken cancellationToken = default);
}

/// <summary>One user as the screen renders it.</summary>
public sealed record UserRowDto(
    string Id,
    string Username,
    string Email,
    string FullName,
    IReadOnlyList<string> Roles,
    bool IsActive,
    bool OnboardingPending);

/// <summary>What staff provisioning needs.</summary>
public sealed record NewStaffUserDto(
    string Username,
    string Email,
    string FirstName,
    string? SecondName,
    string FirstLastName,
    string? SecondLastName,
    string? PhoneNumber,
    string Role,
    string TempPassword);
