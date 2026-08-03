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

        // A duplicate username is a state conflict, not a shape problem — the
        // request is well-formed, it just collides with an existing row. Checked
        // ahead of the unique-index write so the caller gets 409 instead of an
        // untranslated database constraint failure surfacing as a bare 500.
        if (await users.FindByUsernameAsync(request.Username, ct) is not null)
        {
            throw new ConflictException($"Username '{request.Username}' is already taken.");
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
