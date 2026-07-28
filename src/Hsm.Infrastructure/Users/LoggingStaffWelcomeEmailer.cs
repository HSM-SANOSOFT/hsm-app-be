using Hsm.Application.Users;
using Microsoft.Extensions.Logging;

namespace Hsm.Infrastructure.Users;

/// <summary>
/// Staff welcome delivery stub: the frozen system enqueued to the coms queue
/// for the worker to send; the rewritten delivery pipeline arrives with the
/// communications module. Until then delivery is a logged no-op. NEVER logs
/// the temporary password.
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Staff welcome email queued (delivery pending coms module)")]
    private static partial void LogWelcome(ILogger logger);
}
