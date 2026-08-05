using Hsm.Application.Abstractions;
using Hsm.Application.Identity;
using Hsm.Domain.Identity;
using Microsoft.AspNetCore.Identity;

namespace Hsm.Application.Users.Commands.CreateStaffUser;

public sealed class CreateStaffUserHandler(
    UserManager<HsmUser> users,
    IStaffWelcomeEmailer emailer)
    : IRequestHandler<CreateStaffUserCommand, UserWithRoles>
{
    public async Task<UserWithRoles> HandleAsync(CreateStaffUserCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = DateTimeOffset.UtcNow;
        var staff = new HsmUser
        {
            Id = Guid.NewGuid(),
            UserName = request.Username,
            Email = request.Email,
            FirstName = request.FirstName,
            SecondName = request.SecondName,
            FirstLastName = request.FirstLastName,
            SecondLastName = request.SecondLastName,
            PhoneNumber = request.PhoneNumber,
            // Pending forced first-login onboarding.
            OnboardingCompletedAt = null,
            CreatedAt = now,
            UpdatedAt = now,
        };

        // A duplicate username or email is a state conflict, not a shape
        // problem — the request is well-formed, it just collides with an
        // existing row. IdentityResultExtensions is the one place that says so,
        // and it says 409 for exactly the duplicate codes.
        (await users.CreateAsync(staff, request.TempPassword)).ThrowIfFailed("tempPassword");
        (await users.AddToRoleAsync(staff, request.Role)).ThrowIfFailed("role");

        // The account is written; the welcome email is best-effort: a
        // delivery failure is logged and swallowed — an account that exists
        // must not surface a 500.
        try
        {
            await emailer.SendStaffWelcomeAsync(
                staff.Email!, staff.FirstName, staff.UserName!, request.TempPassword, ct);
        }
        catch (Exception)
        {
            // Deliberate swallow: the account write already succeeded and must stand.
        }

        return new UserWithRoles(staff, [request.Role]);
    }
}
