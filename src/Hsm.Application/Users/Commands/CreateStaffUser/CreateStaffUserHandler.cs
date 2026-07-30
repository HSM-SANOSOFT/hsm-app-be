using Hsm.Application.Abstractions;
using Hsm.Application.Auth;
using Hsm.Application.Errors;
using Hsm.Domain.Identity;

namespace Hsm.Application.Users.Commands.CreateStaffUser;

public sealed class CreateStaffUserHandler(
    IUserStore users,
    IPasswordHasher hasher,
    IStaffWelcomeEmailer emailer,
    IAuthUnitOfWork unitOfWork)
    : IRequestHandler<CreateStaffUserCommand, User>
{
    public async Task<User> HandleAsync(CreateStaffUserCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Patient-facing roles never come through this path (frozen guard).
        // Deliberately NOT an IValidator: the frozen envelope for this refusal
        // is a plain 400 (issue.message a string, issue.error "Bad Request"),
        // whereas ValidationBehavior renders the ValidationPipe shape
        // (issue.message an array plus issue.errors). CreateStaffContractTests
        // .Patient_facing_roles_are_rejected pins the former.
        if (!RoleCatalog.IsAssignableToStaff(request.Role))
        {
            throw ApiException.BadRequest(
                "This endpoint provisions staff accounts only; patient/family roles are not allowed");
        }

        // The temp-password hash is computed before any write, as the frozen
        // ResetPasswordHandler does — bcrypt is ~100ms of pure CPU and must not
        // sit between a staged INSERT and its flush.
        var passwordHash = hasher.Hash(request.TempPassword);

        var staff = new User
        {
            Id = Guid.NewGuid(),
            Username = request.Username,
            Email = request.Email,
            PasswordHash = passwordHash,
            FirstName = request.FirstName,
            SecondName = request.SecondName,
            FirstLastName = request.FirstLastName,
            SecondLastName = request.SecondLastName,
            PhoneNumber = request.PhoneNumber,
            // Pending forced first-login onboarding.
            OnboardingCompletedAt = null,
        };
        await users.AddAsync(staff, [request.Role], ct);
        await unitOfWork.SaveChangesAsync(ct);

        // The account is written; the welcome email is best-effort (frozen:
        // enqueue failure was logged and swallowed — an account that exists
        // must not surface a 500).
        try
        {
            await emailer.SendStaffWelcomeAsync(
                staff.Email, staff.FirstName, staff.Username, request.TempPassword, ct);
        }
        catch (Exception)
        {
            // Deliberate swallow, matching the frozen log-and-continue.
        }

        return staff;
    }
}
