using Hsm.Application.Abstractions;

namespace Hsm.Application.Auth.Commands.RefreshTokens;

/// <summary>
/// Refresh rotation (frozen validateRefreshToken + refresh): the presented
/// token must match the single active hash in the mode's own store; success
/// rotates, so the prior token stops working.
///
/// <see cref="AllowAnonymousRequestAttribute"/> because the REFRESH TOKEN is
/// the credential — the request is reachable precisely when the access token
/// is not, which is the whole point of refreshing. The principal here is not a
/// caller-supplied claim: the host validates the refresh token's signature and
/// expiry before constructing this command, and the handler then verifies the
/// raw token against the stored bcrypt hash. Both checks must pass.
/// </summary>
[AllowAnonymousRequest]
public sealed record RefreshTokensCommand(AuthPrincipal Principal, string RawRefreshToken)
    : ICommand<TokenPair>;
