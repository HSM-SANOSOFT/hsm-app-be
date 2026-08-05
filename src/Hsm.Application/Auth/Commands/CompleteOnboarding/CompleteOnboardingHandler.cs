using FluentValidation;
using Hsm.Application.Abstractions;
using Hsm.Application.Errors;
using Hsm.Domain.Identity;
using Microsoft.AspNetCore.Identity;

namespace Hsm.Application.Auth.Commands.CompleteOnboarding;

public sealed class CompleteOnboardingHandler(
    UserManager<HsmUser> users,
    ICurrentPrincipal principal)
    : IRequestHandler<CompleteOnboardingCommand, HsmUser>
{
    public async Task<HsmUser> HandleAsync(CompleteOnboardingCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        // AuthorizationBehavior has already refused an actor-less dispatch;
        // the throw keeps the same 401 if this ever runs outside the pipeline.
        var actor = principal.Actor ?? throw new UnauthorizedException();

        var user = await users.FindByIdAsync(actor.Id);
        if (user is null || user.DeletedAt is not null)
        {
            throw new NotFoundException("User", actor.Id);
        }

        if (!string.Equals(request.ConfirmEmail, user.Email, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException(
                [
                    new FluentValidation.Results.ValidationFailure(
                        "confirmEmail", "Confirmation email does not match the account email."),
                ]);
        }

        if (user.OnboardingCompletedAt is not null)
        {
            throw new ConflictException("Onboarding is already complete.");
        }

        // Remove-then-add rather than a token round trip: the actor IS the
        // account, already authenticated, so there is nothing to prove.
        (await users.RemovePasswordAsync(user)).ThrowIfFailed("newPassword");
        (await users.AddPasswordAsync(user, request.NewPassword)).ThrowIfFailed("newPassword");

        user.PhoneNumber = request.PhoneNumber;
        user.EmailConfirmed = true;
        user.OnboardingCompletedAt = DateTimeOffset.UtcNow;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        (await users.UpdateAsync(user)).ThrowIfFailed("phoneNumber");

        return user;
    }
}
