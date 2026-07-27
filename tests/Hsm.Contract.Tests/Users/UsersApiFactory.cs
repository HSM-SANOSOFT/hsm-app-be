using Hsm.Application.Auth;
using Hsm.Application.Users;
using Hsm.Domain.Identity;
using Hsm.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Hsm.Contract.Tests.Users;

/// <summary>
/// Boots the real host against the real dev-container PostgreSQL on a
/// database DEDICATED to the U13 suites (hsm_users_test) — sibling of the
/// auth factory so the two suites can never collide. Adds deterministic
/// settings seeds (the frozen envValue() fallbacks) and swaps the staff
/// welcome emailer for a capturing double.
/// </summary>
public sealed class UsersApiFactory : WebApplicationFactory<Program>
{
    private static readonly SemaphoreSlim SchemaGate = new(1, 1);
    private static bool _schemaReady;

    public CapturingStaffWelcomeEmailer StaffEmails { get; } = new();

    /// <summary>Env seed for the non-secret SMTP_ADDRESS setting.</summary>
    public const string SeededSmtpAddress = "smtp.seed.contract.test";

    /// <summary>Env seed for the secret SMTP_PASSWORD setting.</summary>
    public const string SeededSmtpPassword = "seeded-smtp-secret";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:HsmDb", UsersConnectionString());
        // HS256 keys must be at least 256 bits.
        builder.UseSetting("Auth:JwtAccessSecret", "contract_test_at_secret_0123456789abcdef");
        builder.UseSetting("Auth:JwtRefreshSecret", "contract_test_rt_secret_0123456789abcdef");
        builder.UseSetting("Auth:CsrfSecret", "contract_test_csrf_secret_0123456789abcdef");
        builder.UseSetting("Auth:Environment", "dev");
        // Frozen setting-definition env seeds (SETTING_DEFINITIONS envValue()).
        builder.UseSetting("Settings:Seed:SMTP_ADDRESS", SeededSmtpAddress);
        builder.UseSetting("Settings:Seed:SMTP_PASSWORD", SeededSmtpPassword);
        builder.ConfigureServices(services =>
            services.AddSingleton<IStaffWelcomeEmailer>(StaffEmails));
    }

    private static string UsersConnectionString()
    {
        var configured = Environment.GetEnvironmentVariable("ConnectionStrings__HsmDb")
            ?? "Host=postgres;Port=5432;Username=postgres;Password=postgres;Database=hsm";
        var withoutDatabase = configured
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => !part.StartsWith("Database=", StringComparison.OrdinalIgnoreCase));
        return string.Join(';', withoutDatabase.Append("Database=hsm_users_test"));
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

    /// <summary>A client that neither follows redirects nor manages cookies.</summary>
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
}

/// <summary>Captures staff welcome emails so tests can read the temp password
/// exactly as the staff member would.</summary>
public sealed class CapturingStaffWelcomeEmailer : IStaffWelcomeEmailer
{
    private readonly List<(string Email, string FirstName, string Username, string TempPassword)> _sent = [];

    public bool FailNext { get; set; }

    public IReadOnlyList<(string Email, string FirstName, string Username, string TempPassword)> Sent
    {
        get
        {
            lock (_sent)
            {
                return [.. _sent];
            }
        }
    }

    public Task SendStaffWelcomeAsync(
        string toEmail, string firstName, string username, string tempPassword, CancellationToken ct = default)
    {
        if (FailNext)
        {
            FailNext = false;
            throw new InvalidOperationException("Simulated delivery failure");
        }

        lock (_sent)
        {
            _sent.Add((toEmail, firstName, username, tempPassword));
        }

        return Task.CompletedTask;
    }
}
