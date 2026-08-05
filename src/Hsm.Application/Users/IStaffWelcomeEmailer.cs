namespace Hsm.Application.Users;

/// <summary>
/// Outbound staff welcome delivery: carries the temporary password to the
/// new staff member's mailbox — the
/// ONLY channel that ever sees it; it is never returned in a response body.
/// Transport arrives with the communications module; this port isolates user
/// administration from that timing, like IRecoveryEmailer does for auth.
/// </summary>
public interface IStaffWelcomeEmailer
{
    Task SendStaffWelcomeAsync(
        string toEmail,
        string firstName,
        string username,
        string tempPassword,
        CancellationToken ct = default);
}
