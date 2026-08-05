using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using Hsm.Api.Identity;
using Hsm.Application.Abstractions;
using Hsm.Domain.Identity;
using Hsm.Infrastructure.Persistence;
using Hsm.Worker;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hsm.Api.Tests;

/// <summary>
/// One data-protection key ring for every host this assembly boots.
///
/// <para>The session cookie is ENCRYPTED, and the shell suites' whole point is
/// that a browser signed in at one door is recognised at the other — so the two
/// in-memory hosts must be able to read each other's cookie. Left alone, each
/// host generates its own ephemeral keys and the sibling sees an undecryptable
/// blob, i.e. an anonymous request. The fix is the one a real multi-instance
/// deployment needs (a shared key ring plus one application discriminator),
/// never a weaker cookie.</para>
///
/// <para>It is a NON-GENERIC holder on purpose. A static field on
/// <see cref="ApiHostFactory{TEntryPoint}"/> would be a field per CLOSED
/// generic type, so <c>Hsm.Api</c>'s hosts and <c>Hsm.Web</c>'s hosts would
/// each get their own "shared" directory and the cross-door proof would fail
/// in exactly the way it is meant to catch.</para>
/// </summary>
internal static class TestKeyRing
{
    /// <summary>The application discriminator both hosts must agree on.</summary>
    public const string ApplicationName = "hsm";

    public static readonly string Directory =
        global::System.IO.Directory.CreateTempSubdirectory("hsm-test-keys").FullName;
}

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

    /// <summary>This factory's queue namespace (Queue:KeyPrefix).</summary>
    protected virtual string JobKeyPrefix => _jobKeyPrefix;

    internal string JobNamespace => JobKeyPrefix;

    /// <summary>Whether this host also PROCESSES what it enqueues (coms/docs suites).</summary>
    protected virtual bool ConsumesJobs => false;

    protected sealed override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:HsmDb", ConnectionStringFor(DatabaseName));
        builder.UseSetting("Queue:KeyPrefix", JobKeyPrefix);
        builder.UseSetting("OpenApi:Enabled", "true");
        builder.ConfigureServices(services => services
            .AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(TestKeyRing.Directory))
            .SetApplicationName(TestKeyRing.ApplicationName));
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

        // The ONE secret this system has, and HS256 requires at least 256 bits
        // of it: integration access tokens are signed with it and the bearer
        // handler verifies with it. The refresh secret went with the refresh
        // JWT — an opaque token has nothing to sign.
        builder.UseSetting("Auth:JwtAccessSecret", "api_test_at_secret_0123456789abcdef");
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
        var users = scope.ServiceProvider.GetRequiredService<UserManager<HsmUser>>();
        var user = new HsmUser
        {
            UserName = username,
            Email = email ?? $"{username}@api.test",
            FirstName = "Api",
            FirstLastName = "Test",
            OnboardingCompletedAt = onboardingCompletedAt,
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var created = await users.CreateAsync(user, password);
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));
        var assigned = await users.AddToRoleAsync(user, role);
        Assert.True(assigned.Succeeded, string.Join("; ", assigned.Errors.Select(e => e.Description)));
        return user.Id;
    }

    /// <summary>The antiforgery header the API's options bind the request token to.</summary>
    public const string AntiforgeryHeader = "X-XSRF-TOKEN";

    /// <summary>
    /// THE AUTH SEAM. Seeds an account in <paramref name="role"/> and returns a
    /// client already carrying its session — and the antiforgery credentials
    /// the middleware requires on every cookie-authenticated mutation, so
    /// callers of this seam can POST/PATCH/DELETE without reimplementing the
    /// handshake per module suite.
    ///
    /// <para>Task 12 replaced both halves of the mechanism underneath this
    /// signature: the session is now the Identity application cookie issued by
    /// POST /api/v1/identity/login, and the forgery token is standard
    /// antiforgery from GET /api/v1/identity/csrf. The SIGNATURE did not change,
    /// which is the whole point — no module suite was rewritten when identity
    /// landed.</para>
    /// </summary>
    public async Task<HttpClient> AuthenticatedClientAsync(
        string role, bool onboarded = true, string? username = null)
    {
        var name = username ?? $"u{Guid.NewGuid():N}"[..20];
        await SeedUserAsync(name, SeedPassword, role, onboarded ? DateTimeOffset.UtcNow : null);
        return await SignedInClientAsync(name, SeedPassword);
    }

    /// <summary>
    /// The same handshake for an account that ALREADY exists — which is what a
    /// second, independent session for one user needs. Each call opens its own
    /// session, so signing one out must not disturb the other.
    /// </summary>
    public async Task<HttpClient> SignedInClientAsync(string name, string password)
    {
        var client = CreateApiClient();
        var response = await client.PostAsJsonAsync(
            "/api/v1/identity/login",
            new { username = name, password },
            CancellationToken.None);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Seed sign-in for '{name}' failed with {(int)response.StatusCode}.");
        }

        var jar = new CookieJar();
        jar.Accept(response);
        client.DefaultRequestHeaders.Add("Cookie", jar.Header);

        // GET /api/v1/identity/csrf stores the cookie half and returns the half
        // the caller must echo in X-XSRF-TOKEN. Both must ride every subsequent
        // mutation on this client, or HsmAntiforgery refuses it with a 403
        // before the pipeline sees the request. It runs AFTER sign-in on
        // purpose: the request token is bound to the authenticated identity.
        var csrf = await client.GetAsync(new Uri("/api/v1/identity/csrf", UriKind.Relative), CancellationToken.None);
        if (!csrf.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Seed antiforgery handshake failed with {(int)csrf.StatusCode}.");
        }

        jar.Accept(csrf);
        client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.Add("Cookie", jar.Header);

        using var body = await csrf.Content.ReadAsStreamAsync(CancellationToken.None);
        var payload = await JsonSerializer.DeserializeAsync<JsonElement>(
            body, cancellationToken: CancellationToken.None);
        client.DefaultRequestHeaders.Add(
            AntiforgeryHeader, payload.GetProperty("token").GetString());

        return client;
    }

    /// <summary>
    /// The little of a browser cookie jar this seam needs: accumulate
    /// Set-Cookie across responses, LAST value per NAME wins.
    ///
    /// <para>Deduplication is not tidiness. The security-stamp validator
    /// rebuilds the principal on every cookie-authenticated request and asks
    /// for the session cookie to be renewed, so the antiforgery response
    /// re-issues <c>hsm.session</c> — and a naive append would send that name
    /// twice in one Cookie header, which is not a request any browser makes and
    /// not a shape the server has to make sense of.</para>
    /// </summary>
    private sealed class CookieJar
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        public string Header => string.Join("; ", _values.Select(pair => $"{pair.Key}={pair.Value}"));

        public void Accept(HttpResponseMessage response)
        {
            if (!response.Headers.TryGetValues("Set-Cookie", out var setCookies))
            {
                return;
            }

            foreach (var pair in setCookies.Select(value => value.Split(';', 2)[0].Split('=', 2)))
            {
                _values[pair[0]] = pair.Length > 1 ? pair[1] : string.Empty;
            }
        }
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
