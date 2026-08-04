using Hsm.Application.Abstractions;
using Hsm.Domain.Identity;

namespace Hsm.Application.Auth.Commands.Login;

/// <summary>
/// Username/password sign-in (frozen validateUser + login). A command, not a
/// query: it writes — the lockout counter on failure, the reset of that counter
/// on success.
///
/// <para>It returns the VERIFIED USER, not a token pair. Browsers get the
/// Identity session cookie, which the endpoint writes from this result; there
/// is no browser refresh token left to rotate. Credential checking stays here
/// rather than in the endpoint because a Blazor circuit signs in through the
/// same command with no HTTP response of its own.</para>
///
/// <see cref="AllowAnonymousRequestAttribute"/> is the whole point of the
/// request — the credentials in the payload ARE the authentication, and there
/// is no principal to gate on. Removing it makes sign-in unreachable and locks
/// every user out.
///
/// <para><see cref="NoAmbientTransactionAttribute"/> is load-bearing for
/// LOCKOUT, and for nothing else. A failed sign-in has to persist the failure
/// and then refuse — but the pipeline's transaction rolls back on exactly that
/// refusal, so under an ambient transaction the failure counter would be
/// discarded by the very request that incremented it and the account would
/// never lock. This handler therefore owns its commits. Removing the attribute
/// silently disables lockout while every sign-in test keeps passing;
/// LockoutTests is what catches it.</para>
/// </summary>
[AllowAnonymousRequest]
[NoAmbientTransaction]
public sealed record LoginCommand(string Username, string Password) : ICommand<HsmUser>;
