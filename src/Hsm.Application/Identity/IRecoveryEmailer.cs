namespace Hsm.Application.Identity;

/// <summary>
/// Outbound recovery email delivery. Delivery transport arrives with the
/// communications module — this port isolates identity from that timing.
/// </summary>
public interface IRecoveryEmailer
{
    /// <summary>
    /// Delivers the reset link for <paramref name="resetToken"/>. The token
    /// must ride in the link's URL fragment and never be persisted or logged.
    /// </summary>
    Task SendPasswordResetAsync(string toEmail, string resetToken, CancellationToken ct = default);

    Task SendUsernameReminderAsync(string toEmail, string username, CancellationToken ct = default);
}
