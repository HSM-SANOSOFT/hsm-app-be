using Hsm.Application.Identity;
using Microsoft.Extensions.Logging;

namespace Hsm.Infrastructure.Identity;

/// <summary>
/// Recovery email delivery stub: a logged no-op, not yet wired to real
/// delivery — the auth contract (generic non-enumerating responses, token
/// persistence) is unaffected. NEVER logs the token or a reset link.
/// </summary>
public sealed partial class LoggingRecoveryEmailer(ILogger<LoggingRecoveryEmailer> logger) : IRecoveryEmailer
{
    public Task SendPasswordResetAsync(string toEmail, string resetToken, CancellationToken ct = default)
    {
        LogReset(logger);
        return Task.CompletedTask;
    }

    public Task SendUsernameReminderAsync(string toEmail, string username, CancellationToken ct = default)
    {
        LogUsername(logger);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Password reset email queued (delivery pending coms module)")]
    private static partial void LogReset(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Username recovery email queued (delivery pending coms module)")]
    private static partial void LogUsername(ILogger logger);
}
