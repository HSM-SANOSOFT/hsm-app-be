using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using Hsm.Api.Auth;
using Hsm.Application.Abstractions;
using Hsm.Application.Auth;
using Hsm.Domain.Identity;
using Hsm.Infrastructure.Persistence;
using Hsm.Worker;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hsm.Api.Tests;

/// <summary>
/// Boots a real host against the dev container's PostgreSQL on a database
/// DEDICATED to the concrete factory, so sibling suites can never collide.
/// Carried over from the retired contract suite: per-suite database,
/// migrate-not-create, per-suite queue namespace, module hooks. Dropped:
/// the frozen JWT/CSRF secret pins that no longer describe anything.
/// </summary>
public abstract class ApiHostFactory<TEntryPoint> : WebApplicationFactory<TEntryPoint>
    where TEntryPoint : class
{
    /// <summary>Password every seeded account shares. Tests never assert on it.</summary>
    public const string SeedPassword = "Seed-Password-1!";

    private static readonly SemaphoreSlim SchemaGate = new(1, 1);
    private static readonly ConcurrentDictionary<string, bool> ReadySchemas = new(StringComparer.Ordinal);

    private readonly string _jobKeyPrefix = $"hsmtest:{Guid.NewGuid():N}";

    /// <summary>The dedicated database this factory's host runs against.</summary>
    protected abstract string DatabaseName { get; }

    /// <summary>The same value, reachable by a sibling host on the same data.</summary>
    internal string Database => DatabaseName;

    /// <summary>This factory's queue namespace (Jobs:KeyPrefix; Queue:KeyPrefix after Task 15).</summary>
    protected virtual string JobKeyPrefix => _jobKeyPrefix;

    internal string JobNamespace => JobKeyPrefix;

    /// <summary>Whether this host also PROCESSES what it enqueues (coms/docs suites).</summary>
    protected virtual bool ConsumesJobs => false;

    protected sealed override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:HsmDb", ConnectionStringFor(DatabaseName));
        builder.UseSetting("Jobs:KeyPrefix", JobKeyPrefix);
        builder.UseSetting("OpenApi:Enabled", "true");
        if (ConsumesJobs)
        {
            builder.ConfigureServices(services =>
            {
                services.AddHsmJobProcessing();

                // A job scope has no HttpContext — the consumer installs the
                // envelope's actor on AmbientPrincipal instead. An HTTP request
                // ALWAYS reads HttpCurrentPrincipal, so an ambient actor can
                // never leak into a route.
                services.AddScoped<HttpCurrentPrincipal>();
                services.AddScoped<ICurrentPrincipal>(sp =>
                    sp.GetRequiredService<IHttpContextAccessor>().HttpContext is null
                        ? sp.GetRequiredService<AmbientPrincipal>()
                        : sp.GetRequiredService<HttpCurrentPrincipal>());
            });
        }

        // HS256 keys must be at least 256 bits. Task 14 reduces these to the
        // integration-token secret alone.
        builder.UseSetting("Auth:JwtAccessSecret", "api_test_at_secret_0123456789abcdef");
        builder.UseSetting("Auth:JwtRefreshSecret", "api_test_rt_secret_0123456789abcdef");
        builder.UseSetting("Auth:CsrfSecret", "api_test_csrf_secret_0123456789abcdef");
        builder.UseSetting("Auth:Environment", "dev");
        ConfigureModule(builder);
    }

    /// <summary>Module-specific settings and service replacements.</summary>
    protected virtual void ConfigureModule(IWebHostBuilder builder)
    {
    }

    internal void ApplyModuleConfiguration(IWebHostBuilder builder) => ConfigureModule(builder);

    private static string ConnectionStringFor(string database)
    {
        var configured = Environment.GetEnvironmentVariable("ConnectionStrings__HsmDb")
            ?? "Host=postgres;Port=5432;Username=postgres;Password=postgres;Database=hsm";
        var withoutDatabase = configured
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => !part.StartsWith("Database=", StringComparison.OrdinalIgnoreCase));
        return string.Join(';', withoutDatabase.Append($"Database={database}"));
    }

    /// <summary>Drops and MIGRATES the dedicated database once per test run.</summary>
    public async Task EnsureSchemaAsync()
    {
        if (ReadySchemas.ContainsKey(DatabaseName))
        {
            return;
        }

        await SchemaGate.WaitAsync();
        try
        {
            if (!ReadySchemas.ContainsKey(DatabaseName))
            {
                using var scope = Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<HsmDbContext>();
                await db.Database.EnsureDeletedAsync();
                await db.Database.MigrateAsync();
                await OnSchemaCreatedAsync();
                ReadySchemas[DatabaseName] = true;
            }
        }
        finally
        {
            SchemaGate.Release();
        }
    }

    /// <summary>Extra one-time provisioning after the schema exists (e.g. buckets).</summary>
    protected virtual Task OnSchemaCreatedAsync() => Task.CompletedTask;

    /// <summary>A client that neither follows redirects nor manages cookies —
    /// cookie behavior is under test.</summary>
    public virtual HttpClient CreateApiClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = false,
    });

    /// <summary>Seeds a user directly and returns its id.</summary>
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
            Email = email ?? $"{username}@api.test",
            PasswordHash = hasher.Hash(password),
            FirstName = "Api",
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

    /// <summary>
    /// THE AUTH SEAM. Seeds an account in <paramref name="role"/> and returns a
    /// client already carrying its session — and the double-submit CSRF
    /// credentials the middleware requires on every cookie-authenticated
    /// mutation, so callers of this seam can POST/PATCH/DELETE without
    /// reimplementing the CSRF handshake per module suite.
    ///
    /// <para>Tasks 2-10 run on the pre-Identity machinery, so the body below
    /// signs in through POST /v1/auth/login and replays the Set-Cookie header.
    /// Task 12 replaces the mechanism (Identity application cookie) and Task 13
    /// replaces the route (/api/v1/identity/login). The SIGNATURE does not
    /// change in either task, which is the whole point: no module suite is
    /// rewritten when identity lands.</para>
    /// </summary>
    public async Task<HttpClient> AuthenticatedClientAsync(
        string role, bool onboarded = true, string? username = null)
    {
        var name = username ?? $"u{Guid.NewGuid():N}"[..20];
        await SeedUserAsync(name, SeedPassword, role, onboarded ? DateTimeOffset.UtcNow : null);

        var client = CreateApiClient();
        var response = await client.PostAsJsonAsync(
            "/v1/auth/login",
            new { username = name, password = SeedPassword },
            CancellationToken.None);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Seed sign-in for role '{role}' failed with {(int)response.StatusCode}.");
        }

        var cookies = (response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.Select(value => value.Split(';', 2)[0])
            : []).ToList();
        client.DefaultRequestHeaders.Add("Cookie", string.Join("; ", cookies));

        // GET /v1/auth/csrf issues the double-submit cookie and returns the
        // matching token; both must ride every subsequent mutation on this
        // client or CsrfProtection.Validate refuses it before the pipeline
        // sees the request.
        var csrf = await client.GetAsync(new Uri("/v1/auth/csrf", UriKind.Relative), CancellationToken.None);
        if (csrf.IsSuccessStatusCode)
        {
            var csrfCookie = csrf.Headers.TryGetValues("Set-Cookie", out var csrfSetCookies)
                ? csrfSetCookies
                    .Select(value => value.Split(';', 2)[0])
                    .FirstOrDefault(value => value.StartsWith($"{CsrfProtection.CookieName}=", StringComparison.Ordinal))
                : null;
            if (csrfCookie is not null)
            {
                cookies.Add(csrfCookie);
                client.DefaultRequestHeaders.Remove("Cookie");
                client.DefaultRequestHeaders.Add("Cookie", string.Join("; ", cookies));
            }

            using var body = await csrf.Content.ReadAsStreamAsync(CancellationToken.None);
            var payload = await JsonSerializer.DeserializeAsync<JsonElement>(body, cancellationToken: CancellationToken.None);
            if (payload.GetProperty("data").TryGetProperty("csrfToken", out var token))
            {
                client.DefaultRequestHeaders.Add(CsrfProtection.HeaderName, token.GetString());
            }
        }

        return client;
    }

    public async Task<T> WithDbAsync<T>(Func<HsmDbContext, Task<T>> work)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HsmDbContext>();
        return await work(db);
    }
}

/// <summary>The REST door. Every API suite boots Hsm.Api.</summary>
public abstract class ApiFactory : ApiHostFactory<Hsm.Api.Program>
{
}
