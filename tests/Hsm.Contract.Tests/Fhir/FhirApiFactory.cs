using Hsm.Application.Auth;
using Hsm.Domain.Identity;
using Hsm.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Hsm.Contract.Tests.Fhir;

/// <summary>
/// Boots the real host against the real dev-container PostgreSQL on a
/// database DEDICATED to the U16 FHIR suites (hsm_fhir_test) — sibling of the
/// other contract factories so the suites can never collide.
/// </summary>
public sealed class FhirApiFactory : WebApplicationFactory<Program>
{
    private static readonly SemaphoreSlim SchemaGate = new(1, 1);
    private static bool _schemaReady;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:HsmDb", ConnectionString());
        // HS256 keys must be at least 256 bits.
        builder.UseSetting("Auth:JwtAccessSecret", "contract_test_at_secret_0123456789abcdef");
        builder.UseSetting("Auth:JwtRefreshSecret", "contract_test_rt_secret_0123456789abcdef");
        builder.UseSetting("Auth:CsrfSecret", "contract_test_csrf_secret_0123456789abcdef");
        builder.UseSetting("Auth:Environment", "dev");
    }

    private static string ConnectionString()
    {
        var configured = Environment.GetEnvironmentVariable("ConnectionStrings__HsmDb")
            ?? "Host=postgres;Port=5432;Username=postgres;Password=postgres;Database=hsm";
        var withoutDatabase = configured
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => !part.StartsWith("Database=", StringComparison.OrdinalIgnoreCase));
        return string.Join(';', withoutDatabase.Append("Database=hsm_fhir_test"));
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
        string username, string password, string role, DateTimeOffset? onboardingCompletedAt)
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
            OnboardingCompletedAt = onboardingCompletedAt,
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

    public async Task<T> WithDbAsync<T>(Func<HsmDbContext, Task<T>> work)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HsmDbContext>();
        return await work(db);
    }
}
