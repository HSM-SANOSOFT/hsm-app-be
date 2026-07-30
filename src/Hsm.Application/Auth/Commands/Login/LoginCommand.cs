using Hsm.Application.Abstractions;

namespace Hsm.Application.Auth.Commands.Login;

/// <summary>
/// Username/password sign-in (frozen validateUser + login). A command, not a
/// query: issuing tokens ROTATES the stored refresh hash, so signing in is a
/// write.
///
/// <see cref="AllowAnonymousRequestAttribute"/> is the whole point of the
/// request — the credentials in the payload ARE the authentication, and there
/// is no principal to gate on. Removing it makes sign-in unreachable and locks
/// every user out.
/// </summary>
[AllowAnonymousRequest]
public sealed record LoginCommand(string Username, string Password) : ICommand<TokenPair>;
