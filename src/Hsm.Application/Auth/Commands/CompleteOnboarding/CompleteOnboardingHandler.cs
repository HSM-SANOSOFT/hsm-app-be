using FluentValidation;
using Hsm.Application.Abstractions;
using Hsm.Application.Errors;

namespace Hsm.Application.Auth.Commands.CompleteOnboarding;

public sealed class CompleteOnboardingHandler(
    IUserStore users,
    IPasswordHasher hasher,
    TokenIssuer issuer,
    IAuthUnitOfWork unitOfWork,
    ICurrentPrincipal principal)
    : IRequestHandler<CompleteOnboardingCommand, TokenPair>
{
    public async Task<TokenPair> HandleAsync(CompleteOnboardingCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        // AuthorizationBehavior has already refused an actor-less dispatch;
        // the throw keeps the same 401 if this ever runs outside the pipeline.
        var actor = principal.Actor ?? throw new UnauthorizedException();
        var userId = Guid.Parse(actor.Id);

        var user = await users.FindByIdAsync(userId, ct)
            ?? throw new NotFoundException("User", actor.Id);

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

        user.PasswordHash = hasher.Hash(request.NewPassword);
        user.PhoneNumber = request.PhoneNumber;
        user.EmailVerified = true;
        user.OnboardingCompletedAt = DateTimeOffset.UtcNow;
        await unitOfWork.SaveChangesAsync(ct);

        return await issuer.IssueAsync(TokenIssuer.PrincipalFor(user), ct);
    }
}
