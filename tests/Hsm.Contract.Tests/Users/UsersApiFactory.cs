using Hsm.Application.Users;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Hsm.Contract.Tests.Users;

/// <summary>
/// The U13 user/settings suite host (hsm_users_test). Adds deterministic
/// settings seeds (the frozen envValue() fallbacks) and swaps the staff
/// welcome emailer for a capturing double.
/// </summary>
public sealed class UsersApiFactory : ContractApiFactory
{
    public CapturingStaffWelcomeEmailer StaffEmails { get; } = new();

    /// <summary>Env seed for the non-secret SMTP_ADDRESS setting.</summary>
    public const string SeededSmtpAddress = "smtp.seed.contract.test";

    /// <summary>Env seed for the secret SMTP_PASSWORD setting.</summary>
    public const string SeededSmtpPassword = "seeded-smtp-secret";

    protected override string DatabaseName => "hsm_users_test";

    protected override void ConfigureModule(IWebHostBuilder builder)
    {
        // Frozen setting-definition env seeds (SETTING_DEFINITIONS envValue()).
        builder.UseSetting("Settings:Seed:SMTP_ADDRESS", SeededSmtpAddress);
        builder.UseSetting("Settings:Seed:SMTP_PASSWORD", SeededSmtpPassword);
        builder.ConfigureServices(services =>
            services.AddSingleton<IStaffWelcomeEmailer>(StaffEmails));
    }
}

/// <summary>Captures staff welcome emails so tests can read the temp password
/// exactly as the staff member would.</summary>
public sealed class CapturingStaffWelcomeEmailer : IStaffWelcomeEmailer
{
    private readonly CapturingSink<(string Email, string FirstName, string Username, string TempPassword)> _sent =
        new("Simulated delivery failure");

    /// <summary>Arms the double so the NEXT delivery throws.</summary>
    public bool FailNext { get; set; }

    public IReadOnlyList<(string Email, string FirstName, string Username, string TempPassword)> Sent =>
        _sent.Snapshot();

    public Task SendStaffWelcomeAsync(
        string toEmail, string firstName, string username, string tempPassword, CancellationToken ct = default)
    {
        if (FailNext)
        {
            FailNext = false;
            _sent.FailNext(1);
        }

        _sent.Record((toEmail, firstName, username, tempPassword));
        return Task.CompletedTask;
    }
}
