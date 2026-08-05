using Hsm.Application.Users;
using Microsoft.Extensions.Logging;

namespace Hsm.Infrastructure.Users;

/// <summary>
/// Staff welcome delivery stub: a logged no-op, not yet wired to real
/// delivery. NEVER logs the temporary password.
/// </summary>
public sealed partial class LoggingStaffWelcomeEmailer(ILogger<LoggingStaffWelcomeEmailer> logger)
    : IStaffWelcomeEmailer
{
    public Task SendStaffWelcomeAsync(
        string toEmail, string firstName, string username, string tempPassword, CancellationToken ct = default)
    {
        LogWelcome(logger);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Staff welcome email not sent — delivery is not yet wired up")]
    private static partial void LogWelcome(ILogger logger);
}
