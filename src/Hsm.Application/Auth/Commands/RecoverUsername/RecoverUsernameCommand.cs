using Hsm.Application.Abstractions;

namespace Hsm.Application.Auth.Commands.RecoverUsername;

/// <summary>
/// Email the username for an account. Silent no-op when unknown — the
/// non-enumeration rule again, and again the reason it is not a validator.
/// Anonymous: someone who cannot remember their username cannot sign in.
/// </summary>
[AllowAnonymousRequest]
public sealed record RecoverUsernameCommand(string Email) : ICommand<Unit>;
