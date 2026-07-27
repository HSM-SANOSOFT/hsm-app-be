using Hsm.Application.Auth;
using Hsm.Application.Coms;
using Hsm.Domain.Identity;
using Hsm.Domain.Templates;
using Hsm.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Hsm.Contract.Tests.Coms;

/// <summary>
/// Boots the real host against the real dev-container PostgreSQL on a
/// database DEDICATED to the U14 coms suites (hsm_coms_test). Swaps the email
/// transport for a capturing double, seeds the mandrill webhook signing key
/// (frozen COMS_WEBHOOK_SIGNING_KEYS), and shortens the dispatcher's retry
/// backoff so failure paths settle within test time.
/// </summary>
public sealed class ComsApiFactory : WebApplicationFactory<Program>
{
    private static readonly SemaphoreSlim SchemaGate = new(1, 1);
    private static bool _schemaReady;

    public const string SigningKey = "coms-contract-test-signing-key";

    public CapturingEmailTransport Transport { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:HsmDb", ConnectionString());
        builder.UseSetting("Auth:JwtAccessSecret", "contract_test_at_secret_0123456789abcdef");
        builder.UseSetting("Auth:JwtRefreshSecret", "contract_test_rt_secret_0123456789abcdef");
        builder.UseSetting("Auth:CsrfSecret", "contract_test_csrf_secret_0123456789abcdef");
        builder.UseSetting("Auth:Environment", "dev");
        builder.UseSetting(
            "Settings:Seed:COMS_WEBHOOK_SIGNING_KEYS",
            $$"""{"mandrill":"{{SigningKey}}"}""");
        builder.UseSetting("Coms:RetryBaseDelayMs", "25");
        builder.ConfigureServices(services => services.AddSingleton<IEmailTransport>(Transport));
    }

    private static string ConnectionString()
    {
        var configured = Environment.GetEnvironmentVariable("ConnectionStrings__HsmDb")
            ?? "Host=postgres;Port=5432;Username=postgres;Password=postgres;Database=hsm";
        var withoutDatabase = configured
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => !part.StartsWith("Database=", StringComparison.OrdinalIgnoreCase));
        return string.Join(';', withoutDatabase.Append("Database=hsm_coms_test"));
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

    public HttpClient CreateApiClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = false,
    });

    public async Task<Guid> SeedUserAsync(string username, string password, string role)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HsmDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = username,
            Email = $"{username}@contract.test",
            PasswordHash = hasher.Hash(password),
            FirstName = "Contract",
            FirstLastName = "Test",
            OnboardingCompletedAt = DateTimeOffset.UtcNow,
            IsActive = true,
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

    /// <summary>Seeds a BASE + EMAIL_INTERNAL template pair directly; returns the email template's name.</summary>
    public async Task<string> SeedEmailTemplateAsync(string? schemaJson = null)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HsmDbContext>();
        var baseTemplate = new Template
        {
            Id = Guid.NewGuid(),
            Category = TemplateCategories.Base,
            Name = $"base_{Guid.NewGuid():N}",
            IsActive = true,
            SchemaJson = "{}",
            Content = "<html>{{{body}}}</html>",
        };
        var email = new Template
        {
            Id = Guid.NewGuid(),
            Category = TemplateCategories.EmailInternal,
            Name = $"email_{Guid.NewGuid():N}",
            IsActive = true,
            SchemaJson = schemaJson ?? """{"patientName":"string"}""",
            Content = "<p>Hola {{patientName}}</p>",
            BaseTemplateId = baseTemplate.Id,
            Email = new TemplateEmail
            {
                Subject = "Hola {{patientName}}",
                FromEmail = "no-reply@hsm.test",
                FromName = "HSM",
            },
        };
        email.Email!.Id = email.Id;
        db.Templates.AddRange(baseTemplate, email);
        await db.SaveChangesAsync();
        return email.Name;
    }

    public async Task<T> WithDbAsync<T>(Func<HsmDbContext, Task<T>> work)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HsmDbContext>();
        return await work(db);
    }
}

/// <summary>
/// Captures outbound emails; can be told to fail the next N sends so
/// dispatch-failure and resend paths are testable.
/// </summary>
public sealed class CapturingEmailTransport : IEmailTransport
{
    private readonly List<OutboundEmail> _sent = [];
    private int _failuresRemaining;

    public IReadOnlyList<OutboundEmail> Sent
    {
        get
        {
            lock (_sent)
            {
                return [.. _sent];
            }
        }
    }

    public void FailNext(int count) => Interlocked.Exchange(ref _failuresRemaining, count);

    public Task<string> SendAsync(OutboundEmail email, CancellationToken ct = default)
    {
        if (Interlocked.Decrement(ref _failuresRemaining) >= 0)
        {
            throw new InvalidOperationException("Simulated SMTP failure");
        }

        lock (_sent)
        {
            _sent.Add(email);
        }

        return Task.FromResult($"<{Guid.NewGuid():N}@contract.test>");
    }
}
