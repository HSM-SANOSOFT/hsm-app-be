using Hsm.Application.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Hsm.Contract.Tests.Auth;

/// <summary>
/// The U12 auth suite host (hsm_auth_test). Each test class gets its own
/// factory (and so its own rate-limiter state). The only service replaced is
/// the recovery emailer — swapped for a capturing double so tests can follow
/// the reset link like a mailbox owner.
/// </summary>
public sealed class AuthApiFactory : ContractApiFactory
{
    public CapturingRecoveryEmailer Emailer { get; } = new();

    protected override string DatabaseName => "hsm_auth_test";

    protected override void ConfigureModule(IWebHostBuilder builder) =>
        builder.ConfigureServices(services =>
            services.AddSingleton<IRecoveryEmailer>(Emailer));

    /// <summary>Signs a token with the host's codec (same secrets) — used to
    /// mint expired/forged variants for negative tests.</summary>
    public string SignToken(AuthPrincipal principal, TokenKind kind, TimeSpan lifetime) =>
        Services.GetRequiredService<IAuthTokenCodec>().Sign(principal, kind, lifetime);
}

/// <summary>Captures recovery emails so tests can act as the mailbox owner.</summary>
public sealed class CapturingRecoveryEmailer : IRecoveryEmailer
{
    private readonly CapturingSink<(string Email, string Token)> _resetTokens = new("Simulated delivery failure");
    private readonly CapturingSink<(string Email, string Username)> _usernameReminders = new("Simulated delivery failure");

    /// <summary>Arms the double so the NEXT delivery (either kind) throws.</summary>
    public bool FailNext { get; set; }

    public IReadOnlyList<(string Email, string Token)> ResetTokens => _resetTokens.Snapshot();

    public IReadOnlyList<(string Email, string Username)> UsernameReminders => _usernameReminders.Snapshot();

    public Task SendPasswordResetAsync(string toEmail, string resetToken, CancellationToken ct = default)
    {
        ArmSink(_resetTokens);
        _resetTokens.Record((toEmail, resetToken));
        return Task.CompletedTask;
    }

    public Task SendUsernameReminderAsync(string toEmail, string username, CancellationToken ct = default)
    {
        ArmSink(_usernameReminders);
        _usernameReminders.Record((toEmail, username));
        return Task.CompletedTask;
    }

    private void ArmSink<T>(CapturingSink<T> sink)
    {
        if (FailNext)
        {
            FailNext = false;
            sink.FailNext(1);
        }
    }
}
