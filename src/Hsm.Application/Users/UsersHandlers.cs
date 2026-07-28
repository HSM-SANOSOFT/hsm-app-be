using Hsm.Application.Auth;
using Hsm.Application.Errors;
using Hsm.Domain.Identity;

namespace Hsm.Application.Users;

/// <summary>
/// Self-service profile update (frozen updateOwnProfile): ONLY firstName and
/// email are reachable through this path — the role and every other column
/// cannot be changed by the profile owner (R6; self-escalation is impossible
/// because the endpoint whitelist rejects a role field outright).
/// </summary>
public sealed class UpdateOwnProfileHandler(IUserStore users, IAuthUnitOfWork unitOfWork)
{
    public async Task<User> HandleAsync(Guid userId, string? firstName, string? email, CancellationToken ct = default)
    {
        var user = await users.FindByIdAsync(userId, ct)
            ?? throw ApiException.NotFound($"User with id {userId} not found");

        var changed = false;
        if (firstName is not null)
        {
            user.FirstName = firstName;
            changed = true;
        }

        if (email is not null)
        {
            user.Email = email;
            changed = true;
        }

        if (changed)
        {
            user.UpdatedAt = DateTimeOffset.UtcNow;
            await unitOfWork.SaveChangesAsync(ct);
        }

        return user;
    }
}

/// <summary>
/// Self-service password change (frozen changeOwnPassword): the current
/// password is bcrypt-verified BEFORE anything is written; a wrong current
/// password fails without touching the stored hash — and without revoking
/// refresh tokens or the active session (only the reset-token flow revokes).
/// </summary>
public sealed class ChangeOwnPasswordHandler(IUserStore users, IPasswordHasher hasher)
{
    public async Task HandleAsync(Guid userId, string currentPassword, string newPassword, CancellationToken ct = default)
    {
        var user = await users.FindByIdAsync(userId, ct)
            ?? throw ApiException.NotFound($"User with id {userId} not found");

        if (!hasher.Verify(currentPassword, user.PasswordHash))
        {
            throw ApiException.Unauthorized("Current password is incorrect");
        }

        await users.UpdatePasswordAsync(userId, hasher.Hash(newPassword), ct);
    }
}

/// <summary>
/// Admin-only staff provisioning (frozen createStaffUser): the account is
/// created pending first-login onboarding (onboardingCompletedAt = null),
/// user row + role rows commit in ONE transaction, and the temporary password
/// is emailed AFTER the commit — never returned in the response.
/// </summary>
public sealed class CreateStaffHandler(
    IUserStore users,
    IPasswordHasher hasher,
    IStaffWelcomeEmailer emailer,
    IAuthUnitOfWork unitOfWork)
{
    public sealed record Command(
        string Username,
        string Email,
        string FirstName,
        string? SecondName,
        string FirstLastName,
        string? SecondLastName,
        string? PhoneNumber,
        string Role,
        string TempPassword);

    public async Task<User> HandleAsync(Command command, CancellationToken ct = default)
    {
        // Patient-facing roles never come through this path (frozen guard).
        if (command.Role is Roles.Patient or Roles.Family)
        {
            throw ApiException.BadRequest(
                "This endpoint provisions staff accounts only; patient/family roles are not allowed");
        }

        var user = await unitOfWork.ExecuteInTransactionAsync(
            async innerCt =>
            {
                var staff = new User
                {
                    Id = Guid.NewGuid(),
                    Username = command.Username,
                    Email = command.Email,
                    PasswordHash = hasher.Hash(command.TempPassword),
                    FirstName = command.FirstName,
                    SecondName = command.SecondName,
                    FirstLastName = command.FirstLastName,
                    SecondLastName = command.SecondLastName,
                    PhoneNumber = command.PhoneNumber,
                    // Pending forced first-login onboarding.
                    OnboardingCompletedAt = null,
                };
                await users.AddAsync(staff, [command.Role], innerCt);
                await unitOfWork.SaveChangesAsync(innerCt);
                return staff;
            },
            ct);

        // The account is committed; the welcome email is best-effort (frozen:
        // enqueue failure was logged and swallowed — an account that exists
        // must not surface a 500).
        try
        {
            await emailer.SendStaffWelcomeAsync(
                user.Email, user.FirstName, user.Username, command.TempPassword, ct);
        }
        catch (Exception)
        {
            // Deliberate swallow, matching the frozen log-and-continue.
        }

        return user;
    }
}

/// <summary>Admin-only paginated listing (frozen findAll): newest first, roles attached.</summary>
public sealed class ListUsersHandler(IUserStore users)
{
    public sealed record Result(IReadOnlyList<User> Users, int Page, int PageSize, int TotalItems);

    public async Task<Result> HandleAsync(int page, int limit, CancellationToken ct = default)
    {
        var (rows, totalItems) = await users.ListAsync(page, limit, ct);
        return new Result(rows, page, limit, totalItems);
    }
}

/// <summary>Admin-only single-user fetch (frozen findUserById).</summary>
public sealed class GetUserHandler(IUserStore users)
{
    public async Task<User> HandleAsync(Guid userId, CancellationToken ct = default) =>
        await users.FindByIdAsync(userId, ct)
            ?? throw ApiException.NotFound($"User with id {userId} not found");
}

/// <summary>
/// Admin-only role change (frozen changeUserRole): replaces the target's role
/// rows via delete-then-insert inside ONE transaction.
///
/// Role change vs. live sessions — PINNED FROZEN BEHAVIOR: authorization is
/// decided from the JWT's own roles claim (frozen RolesGuard), and neither
/// this handler nor the frozen implementation revokes refresh tokens or any
/// server-side cache (none exists — the database row is authoritative
/// immediately). Observable consequences, each pinned by a contract test:
///   * an outstanding access token keeps authorizing with its OLD roles until
///     it expires (user tokens live ≤15 minutes);
///   * /v1/auth/refresh reissues from the refresh token's claims, so even a
///     refreshed pair still carries the OLD roles;
///   * the NEW roles take effect on the next fresh login, where credentials
///     and roles are re-read from the database.
/// </summary>
public sealed class ChangeUserRoleHandler(IUserStore users, IAuthUnitOfWork unitOfWork)
{
    public async Task<User> HandleAsync(Guid userId, string role, CancellationToken ct = default)
    {
        // The endpoint's isIn validation makes this unreachable in practice;
        // kept for parity with the frozen service-level guard.
        if (RoleCatalog.DomainOf(role) is null)
        {
            throw ApiException.BadRequest($"Unknown role '{role}'");
        }

        var user = await users.FindByIdAsync(userId, ct)
            ?? throw ApiException.NotFound($"User with id {userId} not found");

        var replaced = await unitOfWork.ExecuteInTransactionAsync(
            async innerCt =>
            {
                var rows = await users.ReplaceRolesAsync(userId, [role], innerCt);
                await unitOfWork.SaveChangesAsync(innerCt);
                return rows;
            },
            ct);

        // Return the user with the freshly persisted role rows — no re-read.
        user.Roles = [.. replaced];
        return user;
    }
}
