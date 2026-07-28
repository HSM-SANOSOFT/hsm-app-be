using Hsm.Application.Users;
using Hsm.Contracts.Ui;
using Hsm.Domain.Identity;

namespace Hsm.Web.Services;

/// <summary>
/// Host-side user administration (plan U18, screen 2): admin gate first, then
/// the same transactional handlers the frozen /v1/user endpoints call.
/// </summary>
public sealed class UsersAdminUiService(
    UiServiceGate gate,
    ListUsersHandler listUsers,
    CreateStaffHandler createStaff,
    ChangeUserRoleHandler changeRole) : IUsersAdminUiService
{
    public async Task<UserListPageDto> ListUsersAsync(
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        await gate.RequireAdminAsync();
        var result = await listUsers.HandleAsync(page, pageSize, cancellationToken);
        return new UserListPageDto(
            [.. result.Users.Select(ToRow)], result.Page, result.PageSize, result.TotalItems);
    }

    public async Task<UserRowDto> CreateStaffAsync(
        NewStaffUserDto command, CancellationToken cancellationToken = default)
    {
        await gate.RequireAdminAsync();
        var created = await createStaff.HandleAsync(
            new CreateStaffHandler.Command(
                command.Username,
                command.Email,
                command.FirstName,
                command.SecondName,
                command.FirstLastName,
                command.SecondLastName,
                command.PhoneNumber,
                command.Role,
                command.TempPassword),
            cancellationToken);
        return ToRow(created);
    }

    public async Task<UserRowDto> ChangeRoleAsync(
        string userId, string role, CancellationToken cancellationToken = default)
    {
        await gate.RequireAdminAsync();
        if (RoleCatalog.DomainOf(role) is null)
        {
            // The endpoint's isIn validation equivalent: unknown roles are
            // rejected before they reach the handler.
            throw new ArgumentException($"Rol desconocido: '{role}'.", nameof(role));
        }

        var user = await changeRole.HandleAsync(Guid.Parse(userId), role, cancellationToken);
        return ToRow(user);
    }

    public async Task<IReadOnlyList<string>> GetAssignableRolesAsync(
        CancellationToken cancellationToken = default)
    {
        await gate.RequireAdminAsync();
        // Staff provisioning excludes the patient-facing roles (frozen guard).
        return [.. RoleCatalog.All.Where(r => r is not (Roles.Patient or Roles.Family))];
    }

    private static UserRowDto ToRow(User user)
    {
        var fullName = string.Join(
            ' ',
            new[] { user.FirstName, user.SecondName, user.FirstLastName, user.SecondLastName }
                .Where(part => !string.IsNullOrWhiteSpace(part)));
        return new UserRowDto(
            user.Id.ToString(),
            user.Username,
            user.Email,
            fullName,
            [.. user.Roles.Select(r => r.Role)],
            user.IsActive,
            OnboardingPending: user.OnboardingCompletedAt is null);
    }
}
