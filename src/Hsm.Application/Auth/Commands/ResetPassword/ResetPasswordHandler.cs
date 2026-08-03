using Hsm.Application.Abstractions;
using Hsm.Application.Errors;

namespace Hsm.Application.Auth.Commands.ResetPassword;

public sealed class ResetPasswordHandler(
    IUserStore users,
    IPasswordResetTokenStore resetTokens,
    IUserRefreshTokenStore userTokens,
    IPasswordHasher hasher,
    IAuthUnitOfWork unitOfWork)
    : IRequestHandler<ResetPasswordCommand, Unit>
{
    private const string GenericFailure = "Invalid or expired reset token";

    public async Task<Unit> HandleAsync(ResetPasswordCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var row = await resetTokens.FindByHashAsync(TokenDigests.Sha256Hex(request.Token), ct);
        if (row is null || row.UsedAt is not null || row.ExpiresAt < DateTimeOffset.UtcNow)
        {
            throw ApiException.BadRequest(GenericFailure);
        }

        var passwordHash = hasher.Hash(request.NewPassword);

        // TransactionBehavior owns the boundary the frozen handler opened here.
        // Consume FIRST, conditionally on still-unused: two racing requests can
        // never both apply a password.
        if (!await resetTokens.TryConsumeAsync(row.Id, ct))
        {
            throw ApiException.BadRequest(GenericFailure);
        }

        await users.UpdatePasswordAsync(row.UserId, passwordHash, ct);
        // Revoke sessions so a pre-reset stolen session cannot outlive the
        // password change.
        await userTokens.DeactivateActiveAsync(row.UserId, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
