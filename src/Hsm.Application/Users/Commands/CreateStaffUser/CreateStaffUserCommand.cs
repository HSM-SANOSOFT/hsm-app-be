using Hsm.Application.Abstractions;
using Hsm.Domain.Identity;

namespace Hsm.Application.Users.Commands.CreateStaffUser;

/// <summary>
/// Admin-only staff provisioning: the account is
/// created pending first-login onboarding (onboardingCompletedAt = null),
/// user row + role row commit in ONE transaction, and the temporary password
/// is emailed AFTER the write — never returned in the response.
/// </summary>
[RequireRole(Roles.Admin)]
public sealed record CreateStaffUserCommand(
    string Username,
    string Email,
    string FirstName,
    string? SecondName,
    string FirstLastName,
    string? SecondLastName,
    string? PhoneNumber,
    string Role,
    string TempPassword) : ICommand<UserWithRoles>;
