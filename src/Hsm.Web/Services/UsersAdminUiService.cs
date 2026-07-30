using Hsm.Application.Abstractions;
using Hsm.Application.Users.Commands.ChangeUserRole;
using Hsm.Application.Users.Commands.CreateStaffUser;
using Hsm.Application.Users.Queries.ListUsers;
using Hsm.Contracts.Ui;
using Hsm.Domain.Identity;

namespace Hsm.Web.Services;

/// <summary>
/// Host-side user administration (plan U18, screen 2): the same commands and
/// queries the frozen /v1/user endpoints dispatch, through the same pipeline.
/// The admin policy is declared on the request types, so this service performs
/// no authorization of its own — <see cref="UiServiceGate"/> is called only to
/// establish the circuit's actor (and to keep the in-process surface's
/// UnauthorizedAccessException, which the U18 screen tests pin).
/// </summary>
public sealed class UsersAdminUiService(
    UiServiceGate gate,
    IDispatcher dispatcher) : IUsersAdminUiService
{
    public async Task<UserListPageDto> ListUsersAsync(
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        await gate.RequireAdminAsync();
        var result = await dispatcher.Send(new ListUsersQuery(page, pageSize), cancellationToken);
        return new UserListPageDto(
            [.. result.Users.Select(ToRow)], result.Page, result.PageSize, result.TotalItems);
    }

    public async Task<UserRowDto> CreateStaffAsync(
        NewStaffUserDto command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        await gate.RequireAdminAsync();
        var created = await dispatcher.Send(
            new CreateStaffUserCommand(
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
            // rejected before they reach the pipeline.
            throw new ArgumentException($"Rol desconocido: '{role}'.", nameof(role));
        }

        var user = await dispatcher.Send(
            new ChangeUserRoleCommand(Guid.Parse(userId), role), cancellationToken);
        return ToRow(user);
    }

    public async Task<IReadOnlyList<string>> GetAssignableRolesAsync(
        CancellationToken cancellationToken = default)
    {
        // No dispatch here — this is a static catalog read, so it keeps the
        // gate as its own authorization.
        await gate.RequireAdminAsync();
        // Staff provisioning excludes the patient-facing roles (frozen guard).
        return [.. RoleCatalog.All.Where(RoleCatalog.IsAssignableToStaff)];
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
