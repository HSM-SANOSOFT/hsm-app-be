namespace Hsm.Application.Auth;

/// <summary>
/// Frozen account-recovery thresholds (account-recovery.service.ts): reset
/// tokens expire after ONE HOUR and are single-use, and at most FIVE reset
/// requests are accepted per account per rolling hour. These values are
/// behavior.
///
/// <para>Re-homed from the token-digest file Task 11 deleted, whose SHA-256
/// pre-digest existed only because the old password hasher silently truncated
/// its input past 72 bytes. That hasher is gone and so is the reason for the
/// pre-digest — but the thresholds it shared a file with are still
/// contract.</para>
/// </summary>
public static class RecoveryPolicy
{
    public static readonly TimeSpan TokenTtl = TimeSpan.FromHours(1);
    public const int MaxRequestsPerHour = 5;

    /// <summary>
    /// Where the per-account request history lives now that the hand-rolled
    /// <c>password_reset_tokens</c> table is gone: an Identity user token
    /// (AspNetUserTokens / <c>user_tokens</c>), which is the same table
    /// Identity's own reset tokens would use if they were stateful.
    /// </summary>
    public const string RequestLogProvider = "hsm";

    /// <summary>The token name the per-account rolling window is stored under.</summary>
    public const string RequestLogName = "password-reset-requests";
}
