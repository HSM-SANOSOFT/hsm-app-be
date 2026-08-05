using Hsm.Application.Abstractions;

namespace Hsm.Application.Identity.Commands.ResetPassword;

/// <summary>
/// Consume a reset token and set a new password. Invalid, expired, and
/// already-used tokens all fail with the SAME generic message; consumption is
/// atomic and single-use; active sessions are revoked with the change.
///
/// <see cref="AllowAnonymousRequestAttribute"/> because the reset token IS the
/// credential — the caller has by definition lost their password. Removing it
/// makes password recovery unreachable, which is a permanent lockout for
/// anyone who has forgotten theirs.
/// </summary>
[AllowAnonymousRequest]
public sealed record ResetPasswordCommand(string Token, string NewPassword) : ICommand<Unit>;
