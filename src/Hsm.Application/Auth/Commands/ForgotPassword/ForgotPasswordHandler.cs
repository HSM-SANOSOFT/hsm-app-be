using System.Security.Cryptography;
using Hsm.Application.Abstractions;
using Hsm.Application.Errors;
using Hsm.Domain.Identity;

namespace Hsm.Application.Auth.Commands.ForgotPassword;

public sealed class ForgotPasswordHandler(
    IUserStore users,
    IPasswordResetTokenStore resetTokens,
    IRecoveryEmailer emailer,
    IAuthUnitOfWork unitOfWork)
    : IRequestHandler<ForgotPasswordCommand, Unit>
{
    public async Task<Unit> HandleAsync(ForgotPasswordCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await users.FindActiveByEmailAsync(request.Email, ct);
        if (user is null)
        {
            return Unit.Value; // Non-enumerating: nothing observable.
        }

        var since = DateTimeOffset.UtcNow - RecoveryPolicy.TokenTtl;
        var recent = await resetTokens.CountForUserSinceAsync(user.Id, since, ct);
        if (recent >= RecoveryPolicy.MaxRequestsPerHour)
        {
            throw new TooManyRequestsException();
        }

        var plaintext = RandomNumberGenerator.GetHexString(64, lowercase: true);
        var tokenHash = TokenDigests.Sha256Hex(plaintext);
        await resetTokens.AddAsync(
            new PasswordResetToken
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                TokenHash = tokenHash,
                ExpiresAt = DateTimeOffset.UtcNow + RecoveryPolicy.TokenTtl,
                UsedAt = null,
            },
            ct);
        await unitOfWork.SaveChangesAsync(ct);

        try
        {
            // The plaintext token is handed to the mailer and NEVER persisted.
            await emailer.SendPasswordResetAsync(user.Email, plaintext, ct);
        }
        catch (Exception)
        {
            // Compensate: drop the unusable token so it doesn't burn a
            // rate-limit slot, and stay generic — surfacing a 500 only for
            // known accounts would break non-enumeration.
            await resetTokens.DeleteByHashAsync(tokenHash, ct);
            await unitOfWork.SaveChangesAsync(ct);
        }

        return Unit.Value;
    }
}
