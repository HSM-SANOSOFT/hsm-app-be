using Hsm.Application.Coms;
using Microsoft.Extensions.Logging;

namespace Hsm.Infrastructure.Coms;

/// <summary>
/// The default <see cref="IEmailTransport"/>: SMTP delivery is deployment
/// configuration (the SMTP_* settings); in this repo the adapter logs the
/// send and fabricates a provider message id. Real relays are wired per
/// deployment behind the same port.
/// </summary>
public sealed partial class LoggingEmailTransport(ILogger<LoggingEmailTransport> logger) : IEmailTransport
{
    public Task<string> SendAsync(OutboundEmail email, CancellationToken ct = default)
    {
        var messageId = $"<{Guid.NewGuid():N}@hsm.local>";
        var recipients = string.Join(", ", email.To);
        LogSend(logger, recipients, email.Subject, messageId);
        return Task.FromResult(messageId);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Email dispatched to {To} (subject: {Subject}, messageId: {MessageId})")]
    private static partial void LogSend(ILogger logger, string to, string subject, string messageId);
}
