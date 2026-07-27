using Hsm.Application.Auth;
using Hsm.Domain.Identity;
using Hsm.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Hsm.Contract.Tests.Auth;

/// <summary>
/// Boots the real host against the real dev-container PostgreSQL, on a
/// dedicated database (hsm_auth_test). Each test class gets its own factory
/// (and so its own rate-limiter state); the schema is created once per run.
/// The only service replaced is the recovery emailer — swapped for a
/// capturing double so tests can follow the reset link like a mailbox owner.
/// </summary>
public sealed class AuthApiFactory : WebApplicationFactory<Program>
{
    private static readonly SemaphoreSlim SchemaGate = new(1, 1);
    private static bool _schemaReady;

    public CapturingRecoveryEmailer Emailer { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:HsmDb", AuthConnectionString());
        // HS256 keys must be at least 256 bits.
        builder.UseSetting("Auth:JwtAccessSecret", "contract_test_at_secret_0123456789abcdef");
        builder.UseSetting("Auth:JwtRefreshSecret", "contract_test_rt_secret_0123456789abcdef");
        builder.UseSetting("Auth:CsrfSecret", "contract_test_csrf_secret_0123456789abcdef");
        builder.UseSetting("Auth:Environment", "dev");
        builder.ConfigureServices(services =>
            services.AddSingleton<IRecoveryEmailer>(Emailer));
    }

    /// <summary>
    /// The dev container's Postgres by default, CI's via the same
    /// ConnectionStrings__HsmDb env override the integration tests use —
    /// always on a DEDICATED database so runs can't collide with
    /// Hsm.Integration.Tests.
    /// </summary>
    private static string AuthConnectionString()
    {
        var configured = Environment.GetEnvironmentVariable("ConnectionStrings__HsmDb")
            ?? "Host=postgres;Port=5432;Username=postgres;Password=postgres;Database=hsm";
        var withoutDatabase = configured
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => !part.StartsWith("Database=", StringComparison.OrdinalIgnoreCase));
        return string.Join(';', withoutDatabase.Append("Database=hsm_auth_test"));
    }

    public async Task EnsureSchemaAsync()
    {
        if (_schemaReady)
        {
            return;
        }

        await SchemaGate.WaitAsync();
        try
        {
            if (!_schemaReady)
            {
                using var scope = Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<HsmDbContext>();
                await db.Database.EnsureDeletedAsync();
                await db.Database.EnsureCreatedAsync();
                _schemaReady = true;
            }
        }
        finally
        {
            SchemaGate.Release();
        }
    }

    /// <summary>A client that neither follows redirects nor manages cookies —
    /// cookie behavior is part of the contract under test.</summary>
    public HttpClient CreateApiClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = false,
    });

    /// <summary>Seeds a user directly (bcrypt-hashed) and returns its id.</summary>
    public async Task<Guid> SeedUserAsync(
        string username,
        string password,
        string role,
        DateTimeOffset? onboardingCompletedAt,
        string? email = null,
        bool isActive = true)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HsmDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = username,
            Email = email ?? $"{username}@contract.test",
            PasswordHash = hasher.Hash(password),
            FirstName = "Contract",
            FirstLastName = "Test",
            OnboardingCompletedAt = onboardingCompletedAt,
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        user.Roles.Add(new UserRole
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Role = role,
            Domain = RoleCatalog.DomainOf(role) ?? "System",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    public async Task<T> WithDbAsync<T>(Func<HsmDbContext, Task<T>> work)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HsmDbContext>();
        return await work(db);
    }

    /// <summary>Signs a token with the host's codec (same secrets) — used to
    /// mint expired/forged variants for negative tests.</summary>
    public string SignToken(AuthPrincipal principal, TokenKind kind, TimeSpan lifetime) =>
        Services.GetRequiredService<IAuthTokenCodec>().Sign(principal, kind, lifetime);
}

/// <summary>Captures recovery emails so tests can act as the mailbox owner.</summary>
public sealed class CapturingRecoveryEmailer : IRecoveryEmailer
{
    private readonly List<(string Email, string Token)> _resetTokens = [];
    private readonly List<(string Email, string Username)> _usernameReminders = [];

    public bool FailNext { get; set; }

    public IReadOnlyList<(string Email, string Token)> ResetTokens
    {
        get
        {
            lock (_resetTokens)
            {
                return [.. _resetTokens];
            }
        }
    }

    public IReadOnlyList<(string Email, string Username)> UsernameReminders
    {
        get
        {
            lock (_usernameReminders)
            {
                return [.. _usernameReminders];
            }
        }
    }

    public Task SendPasswordResetAsync(string toEmail, string resetToken, CancellationToken ct = default)
    {
        ThrowIfArmed();
        lock (_resetTokens)
        {
            _resetTokens.Add((toEmail, resetToken));
        }

        return Task.CompletedTask;
    }

    public Task SendUsernameReminderAsync(string toEmail, string username, CancellationToken ct = default)
    {
        ThrowIfArmed();
        lock (_usernameReminders)
        {
            _usernameReminders.Add((toEmail, username));
        }

        return Task.CompletedTask;
    }

    private void ThrowIfArmed()
    {
        if (FailNext)
        {
            FailNext = false;
            throw new InvalidOperationException("Simulated delivery failure");
        }
    }
}
