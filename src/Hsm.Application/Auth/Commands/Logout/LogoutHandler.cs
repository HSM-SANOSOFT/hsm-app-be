using Hsm.Application.Abstractions;
using Hsm.Application.Errors;

namespace Hsm.Application.Auth.Commands.Logout;

public sealed class LogoutHandler(
    IAuthTokenCodec codec,
    IUserRefreshTokenStore userTokens,
    IAuthUnitOfWork unitOfWork)
    : IRequestHandler<LogoutCommand, Unit>
{
    public async Task<Unit> HandleAsync(LogoutCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrEmpty(request.Token))
        {
            throw new UnauthorizedException("Token not found");
        }

        var principal = (await codec.ValidateAsync(request.Token, TokenKind.Access, ignoreExpiration: true)).Principal
            ?? (await codec.ValidateAsync(request.Token, TokenKind.Refresh, ignoreExpiration: true)).Principal
            ?? throw new UnauthorizedException("Invalid token");

        var affected = await userTokens.DeactivateActiveAsync(Guid.Parse(principal.Id), ct);
        if (affected == 0)
        {
            throw new ConflictException("No active session to end.");
        }

        await unitOfWork.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
