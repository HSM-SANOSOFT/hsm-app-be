using Hsm.Application.Abstractions;

namespace Hsm.Application.Auth.Commands.Logout;

/// <summary>
/// Sign-out (frozen logout): accepts an access OR refresh token (expired is
/// fine — sign-out must always be possible) and deactivates the USER store
/// rows for its subject.
///
/// <see cref="AllowAnonymousRequestAttribute"/> for the same reason
/// <c>RefreshTokensCommand</c> carries it: the TOKEN IS THE CREDENTIAL, and
/// this handler is the thing that verifies it. There is no principal for the
/// pipeline to gate on — the frozen route runs no edge authentication at all,
/// deliberately, because an expired or otherwise unusable token must still be
/// able to end a session. Anonymous here does not mean unauthenticated: the
/// handler rejects a missing token (401 "Token not found") and an unverifiable
/// one (401 "Invalid token"), and only ever deactivates rows belonging to the
/// subject named inside the presented token.
///
/// It is <see cref="AllowPendingOnboarding"/> in spirit too — a pending user
/// must be able to sign out — but the anonymous policy already subsumes that.
/// </summary>
[AllowAnonymousRequest]
public sealed record LogoutCommand(string? Token) : ICommand<Unit>;
