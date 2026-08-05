using Hsm.Application.Abstractions;
using Hsm.Application.Users;
using Hsm.Application.Users.Commands.ChangeUserRole;
using Hsm.Application.Users.Commands.CreateStaffUser;
using Hsm.Application.Users.Queries.ListUsers;
using Hsm.Contracts;
using Hsm.Contracts.Ui;
using Hsm.Domain.Identity;
using Hsm.Web.Auth;

namespace Hsm.Web.Services;

/// <summary>
/// Host-side user administration (plan U18, screen 2): the same commands and
/// queries the /v1/user endpoints dispatch, through the same pipeline.
/// The admin policy is declared on the request types and enforced by the
/// pipeline, so this service performs no authorization of its own —
/// <see cref="ShellActor"/> only publishes WHO is calling. A non-admin
/// circuit therefore fails with the pipeline's ForbiddenException (403), the
/// same refusal the REST surface renders, rather than a UI-local exception type.
/// </summary>
public sealed class UsersAdminUiService(
    ShellActor shellActor,
    IDispatcher dispatcher) : IUsersAdminUiService
{
    public async Task<PagedResult<UserRowDto>> ListUsersAsync(
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        await shellActor.InstallAsync(cancellationToken);
        var result = await dispatcher.Send(new ListUsersQuery(page, pageSize), cancellationToken);
        return result.Map(ToRow);
    }

    public async Task<UserRowDto> CreateStaffAsync(
        NewStaffUserDto command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        await shellActor.InstallAsync(cancellationToken);
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
        await shellActor.InstallAsync(cancellationToken);
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
        // No dispatch, so no policy: this reads a compile-time constant
        // (RoleCatalog) and discloses nothing about any account. The screen
        // that calls it is [Authorize(Roles = admin)] at the routing layer.
        // Staff provisioning excludes the patient-facing roles.
        return [.. RoleCatalog.All.Where(RoleCatalog.IsAssignableToStaff)];
    }

    private static UserRowDto ToRow(UserWithRoles projection)
    {
        var user = projection.User;
        var fullName = string.Join(
            ' ',
            new[] { user.FirstName, user.SecondName, user.FirstLastName, user.SecondLastName }
                .Where(part => !string.IsNullOrWhiteSpace(part)));
        return new UserRowDto(
            user.Id.ToString(),
            user.UserName ?? string.Empty,
            user.Email ?? string.Empty,
            fullName,
            projection.Roles,
            user.IsActive,
            OnboardingPending: user.OnboardingCompletedAt is null);
    }
}
