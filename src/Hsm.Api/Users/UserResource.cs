using Hsm.Domain.Identity;

namespace Hsm.Api.Users;

/// <summary>
/// The wire shape of a user. Roles flatten to a string array: the role ROW's
/// id, domain and creation timestamp are internal bookkeeping and no caller has
/// ever had a use for them. PasswordHash and DeletedAt are absent by
/// construction — this record is the allow-list, so a new column on the entity
/// cannot leak by being forgotten.
/// </summary>
public sealed record UserResource(
    Guid Id,
    string Username,
    string Email,
    string FirstName,
    string? SecondName,
    string FirstLastName,
    string? SecondLastName,
    string? PhoneNumber,
    string? Gender,
    IReadOnlyList<string> Roles,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset? OnboardingCompletedAt,
    bool IsActive,
    bool EmailVerified,
    bool PhoneVerified,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static UserResource From(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return new UserResource(
            user.Id,
            user.Username,
            user.Email,
            user.FirstName,
            user.SecondName,
            user.FirstLastName,
            user.SecondLastName,
            user.PhoneNumber,
            user.Gender,
            [.. user.Roles.Select(r => r.Role)],
            user.LastLoginAt,
            user.OnboardingCompletedAt,
            user.IsActive,
            user.EmailVerified,
            user.PhoneVerified,
            user.CreatedAt,
            user.UpdatedAt);
    }
}

/// <summary>Request bodies. Bound by System.Text.Json; validated by the pipeline.</summary>
public sealed record CreateUserRequest(
    string Username,
    string Email,
    string FirstName,
    string? SecondName,
    string FirstLastName,
    string? SecondLastName,
    string? PhoneNumber,
    string Role,
    string TempPassword);

public sealed record UpdateUserRoleRequest(string Role);

public sealed record UpdateOwnProfileRequest(string? FirstName, string? Email);

public sealed record ChangeOwnPasswordRequest(string CurrentPassword, string NewPassword);
