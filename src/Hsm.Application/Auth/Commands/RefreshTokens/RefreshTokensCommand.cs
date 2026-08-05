using Hsm.Application.Abstractions;

namespace Hsm.Application.Auth.Commands.RefreshTokens;

/// <summary>
/// INTEGRATION refresh rotation (frozen validateRefreshToken + refresh): the
/// presented token must match the single active hash in the integration store;
/// success rotates, so the prior token stops working. The handler refuses a
/// non-integration principal outright — see the comment on that check for why
/// re-signing a human's claims would be a privilege-escalation hole.
///
/// <see cref="AllowAnonymousRequestAttribute"/> because the REFRESH TOKEN is
/// the credential — the request is reachable precisely when the access token
/// is not, which is the whole point of refreshing. The principal here is not a
/// caller-supplied claim: the host validates the refresh token's signature and
/// expiry before constructing this command, and the handler then verifies the
/// raw token against the stored hash. Both checks must pass.
/// </summary>
[AllowAnonymousRequest]
public sealed record RefreshTokensCommand(AuthPrincipal Principal, string RawRefreshToken)
    : ICommand<TokenPair>;
