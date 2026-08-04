using Hsm.Application.Abstractions;
using Hsm.Application.Auth;
using Hsm.Application.Auth.Commands.ForgotPassword;
using Hsm.Application.Auth.Commands.RecoverUsername;
using Hsm.Application.Auth.Commands.ResetPassword;
using Hsm.Application.Errors;
using Hsm.Domain.Identity;
using Hsm.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hsm.Integration.Tests;

/// <summary>
/// The password-recovery round trip against real PostgreSQL, end to end:
/// request a reset, take the link the mailer was handed, spend it, sign in with
/// the new password.
///
/// <para>Task 11 rebuilt this flow on Identity and it is the part of that task
/// with the least in common with what it replaced. The hand-rolled
/// <c>password_reset_tokens</c> table is gone: Identity's reset token is a
/// data-protected blob that persists nothing, is bound to the user's security
/// stamp, and is therefore spent by the reset itself. Two consequences are only
/// visible from a test like this one — the emailed value has to carry the user
/// id (the wire shape is <c>{ token, newPassword }</c> with no account field,
/// and ResetPasswordAsync must know whose token it is), and the per-account
/// rolling-hour limit had to find a new home now that there are no rows to
/// count.</para>
/// </summary>
public sealed class PasswordRecoveryTests : IAsyncLifetime
{
    private ServiceProvider _provider = null!;
    private RecordingEmailer _emailer = null!;

    /// <summary>Captures what would have been emailed; the plaintext link never leaves here.</summary>
    private sealed class RecordingEmailer : IRecoveryEmailer
    {
        public List<string> ResetLinks { get; } = [];

        public List<string> Usernames { get; } = [];

        public bool Throw { get; set; }

        public Task SendPasswordResetAsync(string toEmail, string resetToken, CancellationToken ct = default)
        {
            if (Throw)
            {
                throw new InvalidOperationException("relay down");
            }

            ResetLinks.Add(resetToken);
            return Task.CompletedTask;
        }

        public Task SendUsernameReminderAsync(string toEmail, string username, CancellationToken ct = default)
        {
            Usernames.Add(username);
            return Task.CompletedTask;
        }
    }

    public async Task InitializeAsync()
    {
        var configured = TestServices.Configuration["ConnectionStrings:HsmDb"]!;
        var dedicated = string.Join(
            ';',
            configured
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(part => !part.StartsWith("Database=", StringComparison.OrdinalIgnoreCase))
                .Append("Database=hsm_recovery_test"));

        _emailer = new RecordingEmailer();
        _provider = TestServices.Build(
            builder => builder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:HsmDb"] = dedicated,
            }),
            services =>
            {
                services.AddHsmPipeline();
                services.AddScoped<ICurrentPrincipal, AmbientPrincipal>();
                services.AddSingleton<IRecoveryEmailer>(_emailer);
            });

        await TestServices.RecreateAsync(_provider);
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    [Fact]
    public async Task A_reset_link_sets_the_new_password_and_cannot_be_spent_twice()
    {
        var (email, _) = await SeedAsync();

        await SendAsync(new ForgotPasswordCommand(email));
        var link = Assert.Single(_emailer.ResetLinks);

        await SendAsync(new ResetPasswordCommand(link, "Reset-Passw0rd-1"));
        Assert.True(await CanSignInAsync(email, "Reset-Passw0rd-1"));

        // The security stamp moved with the reset, so the same link is now
        // refused — the single-use guarantee the deleted usedAt column gave.
        var replay = await Assert.ThrowsAsync<FluentValidation.ValidationException>(
            () => SendAsync(new ResetPasswordCommand(link, "Reset-Passw0rd-2")));
        Assert.Equal("token", Assert.Single(replay.Errors).PropertyName);
        Assert.True(await CanSignInAsync(email, "Reset-Passw0rd-1"));
    }

    [Theory]
    [InlineData("not-a-link")]
    [InlineData("00000000000000000000000000000000.bad-token")]
    [InlineData(".")]
    public async Task A_malformed_or_unknown_link_is_one_generic_field_failure(string link)
    {
        // Every unusable link fails the same way — a caller must not be able to
        // tell a mistyped token from one naming an account that does not exist.
        var refusal = await Assert.ThrowsAsync<FluentValidation.ValidationException>(
            () => SendAsync(new ResetPasswordCommand(link, "Reset-Passw0rd-1")));

        var failure = Assert.Single(refusal.Errors);
        Assert.Equal("token", failure.PropertyName);
        Assert.Equal("The password reset link is invalid or has expired.", failure.ErrorMessage);
    }

    [Fact]
    public async Task An_unknown_email_is_a_silent_no_op_on_both_recovery_routes()
    {
        await SendAsync(new ForgotPasswordCommand($"{Guid.NewGuid():N}@nobody.test"));
        await SendAsync(new RecoverUsernameCommand($"{Guid.NewGuid():N}@nobody.test"));

        Assert.Empty(_emailer.ResetLinks);
        Assert.Empty(_emailer.Usernames);
    }

    [Fact]
    public async Task Username_recovery_emails_the_account_name()
    {
        var (email, username) = await SeedAsync();

        await SendAsync(new RecoverUsernameCommand(email));

        Assert.Equal(username, Assert.Single(_emailer.Usernames));
    }

    [Fact]
    public async Task A_sixth_reset_request_within_the_hour_is_refused()
    {
        var (email, _) = await SeedAsync();

        for (var request = 0; request < RecoveryPolicy.MaxRequestsPerHour; request++)
        {
            await SendAsync(new ForgotPasswordCommand(email));
        }

        await Assert.ThrowsAsync<TooManyRequestsException>(
            () => SendAsync(new ForgotPasswordCommand(email)));
        Assert.Equal(RecoveryPolicy.MaxRequestsPerHour, _emailer.ResetLinks.Count);
    }

    [Fact]
    public async Task A_link_that_could_not_be_delivered_does_not_burn_a_rate_limit_slot()
    {
        // The frozen handler deleted the unusable token so it would not count.
        // There is no token row to delete now, so the attempt is simply never
        // recorded — same outcome, and the caller still sees nothing.
        var (email, _) = await SeedAsync();

        _emailer.Throw = true;
        for (var request = 0; request < RecoveryPolicy.MaxRequestsPerHour + 3; request++)
        {
            await SendAsync(new ForgotPasswordCommand(email));
        }

        _emailer.Throw = false;
        await SendAsync(new ForgotPasswordCommand(email));

        Assert.Single(_emailer.ResetLinks);
    }

    [Fact]
    public async Task Two_simultaneous_reset_requests_for_one_account_both_succeed()
    {
        // The rate-limit window lives in ONE row per account, so the first-ever
        // pair of concurrent requests both find no row and both try to create
        // it. The loser must not surface — a 500 here is an enumeration oracle:
        // it happens only for an account that EXISTS and has never requested a
        // reset, which is the exact state an enumerator probes.
        var (email, _) = await SeedAsync();

        var first = SendAsync(new ForgotPasswordCommand(email));
        var second = SendAsync(new ForgotPasswordCommand(email));

        await Task.WhenAll(first, second);
    }

    [Fact]
    public async Task Recovery_resolves_the_LIVE_account_when_a_soft_deleted_one_shares_its_email()
    {
        // The unique indexes on users are filtered on DeletedAt precisely so a
        // soft-deleted account frees its email for reuse — which means two rows
        // CAN hold the same email, and an unfiltered FirstOrDefault may return
        // either. Recovery must always resolve the live one.
        var (email, _) = await SeedAsync();
        await SoftDeleteAsync(email);
        var username = await SeedWithEmailAsync(email);

        await SendAsync(new RecoverUsernameCommand(email));

        Assert.Equal(username, Assert.Single(_emailer.Usernames));
    }

    [Fact]
    public async Task A_soft_deleted_account_gets_no_recovery_email_at_all()
    {
        var (email, _) = await SeedAsync();
        await SoftDeleteAsync(email);

        await SendAsync(new RecoverUsernameCommand(email));
        await SendAsync(new ForgotPasswordCommand(email));

        Assert.Empty(_emailer.Usernames);
        Assert.Empty(_emailer.ResetLinks);
    }

    private async Task SoftDeleteAsync(string email)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HsmDbContext>();
        var normalized = email.ToUpperInvariant();
        await db.Users.Where(u => u.NormalizedEmail == normalized && u.DeletedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(u => u.DeletedAt, DateTimeOffset.UtcNow),
                CancellationToken.None);
    }

    private async Task<(string Email, string Username)> SeedAsync()
    {
        using var scope = _provider.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<HsmUser>>();
        var username = $"rec_{Guid.NewGuid():N}";
        var email = $"{Guid.NewGuid():N}@recovery.test";
        var user = new HsmUser
        {
            UserName = username,
            Email = email,
            FirstName = "Rec",
            FirstLastName = "Overy",
            OnboardingCompletedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var created = await users.CreateAsync(user, "Recovery-Passw0rd");
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));
        return (email, username);
    }

    /// <summary>
    /// Re-provisions the freed email on a NEW live row, written through the
    /// DbContext rather than UserManager.CreateAsync.
    ///
    /// <para>Not a shortcut — CreateAsync cannot do this yet. Identity's
    /// UserValidator enforces RequireUniqueEmail through the same unfiltered
    /// FindByEmailAsync this suite is about, so it still sees the soft-deleted
    /// row and refuses. That is a FOURTH consumer of the unfiltered lookup,
    /// found by this test and reported as a follow-up; it is a provisioning
    /// concern, while what is under test here is whether RECOVERY resolves the
    /// live row when two exist. The database itself allows the pair: the unique
    /// indexes are filtered on DeletedAt.</para>
    /// </summary>
    private async Task<string> SeedWithEmailAsync(string email)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HsmDbContext>();
        var username = $"rec_{Guid.NewGuid():N}";
        db.Users.Add(new HsmUser
        {
            Id = Guid.NewGuid(),
            UserName = username,
            NormalizedUserName = username.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString(),
            FirstName = "Live",
            FirstLastName = "Again",
            OnboardingCompletedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(CancellationToken.None);
        return username;
    }

    private async Task<Unit> SendAsync(ICommand<Unit> command)
    {
        using var scope = _provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IDispatcher>()
            .Send(command, CancellationToken.None);
    }

    private async Task<bool> CanSignInAsync(string email, string password)
    {
        using var scope = _provider.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<HsmUser>>();
        var user = await users.FindByEmailAsync(email);
        return user is not null && await users.CheckPasswordAsync(user, password);
    }
}
