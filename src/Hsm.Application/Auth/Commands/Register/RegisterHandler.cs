using Hsm.Application.Abstractions;
using Hsm.Domain.Identity;
using Microsoft.AspNetCore.Identity;

namespace Hsm.Application.Auth.Commands.Register;

public sealed class RegisterHandler(UserManager<HsmUser> users)
    : IRequestHandler<RegisterCommand, HsmUser>
{
    public async Task<HsmUser> HandleAsync(RegisterCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = DateTimeOffset.UtcNow;
        var user = new HsmUser
        {
            Id = Guid.NewGuid(),
            UserName = request.Username,
            Email = request.Email,
            FirstName = request.FirstName,
            FirstLastName = request.FirstLastName,
            SecondName = request.SecondName,
            SecondLastName = request.SecondLastName,
            PhoneNumber = request.PhoneNumber,
            Gender = request.Gender,
            // Patients never do the staff first-login flow.
            OnboardingCompletedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };

        // TransactionBehavior owns the boundary, and UserManager's store shares
        // this scope's DbContext — so the user row and its role row commit or
        // roll back as one. A duplicate username or a policy-failing password
        // surfaces as a conflict or a field failure through ThrowIfFailed
        // rather than as a unique-index violation.
        (await users.CreateAsync(user, request.Password)).ThrowIfFailed("password");
        (await users.AddToRoleAsync(user, Roles.Patient)).ThrowIfFailed("role");
        return user;
    }
}
