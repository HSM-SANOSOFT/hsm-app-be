using System.Collections.Concurrent;
using Hsm.Api.Auth;
using Hsm.Application.Abstractions;
using Hsm.Application.Auth;
using Hsm.Domain.Identity;
using Hsm.Infrastructure.Persistence;
using Hsm.Worker;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Hsm.Contract.Tests;

/// <summary>
/// What <see cref="ContractTest{TFactory}"/> needs of a booted host, whichever
/// door it is. Task 17 split the application into two hosts, so the base can no
/// longer name a single entry point.
/// </summary>
public interface IContractHost
{
    /// <summary>Recreates the dedicated database once per test run.</summary>
    Task EnsureSchemaAsync();

    /// <summary>A client that neither follows redirects nor manages cookies —
    /// cookie behavior is part of the contract under test.</summary>
    HttpClient CreateApiClient();

    /// <summary>Seeds a user directly (bcrypt-hashed) and returns its id.</summary>
    Task<Guid> SeedUserAsync(
        string username,
        string password,
        string role,
        DateTimeOffset? onboardingCompletedAt,
        string? email = null,
        bool isActive = true);
}

/// <summary>
/// Base for the contract-test hosts: boots the real application against the
/// real dev-container PostgreSQL on a database DEDICATED to the concrete
/// factory (sibling suites can never collide), with the shared JWT/CSRF/env
/// settings every suite pins. Module-specific settings and service doubles go
/// through <see cref="ConfigureModule"/>.
/// </summary>
public abstract class ContractHostFactory<TEntryPoint> : WebApplicationFactory<TEntryPoint>, IContractHost
    where TEntryPoint : class
{
    private static readonly SemaphoreSlim SchemaGate = new(1, 1);
    private static readonly ConcurrentDictionary<string, bool> ReadySchemas = new(StringComparer.Ordinal);

    private readonly string _jobKeyPrefix = $"hsmtest:{Guid.NewGuid():N}";

    /// <summary>The dedicated database this factory's host runs against.</summary>
    protected abstract string DatabaseName { get; }

    /// <summary>The same value, reachable by a sibling host on the same data.</summary>
    internal string Database => DatabaseName;

    /// <summary>
    /// The queue namespace this factory's host enqueues into
    /// (<c>Jobs:KeyPrefix</c>). Production runs ONE namespace for the whole
    /// deployment — that is the point of the worker being the consumer — but
    /// several factories boot in one test process against one Redis, each with
    /// its own transport double, and each asserts on the jobs IT enqueued (one
    /// suite arms its transport to fail the next five sends and requires all
    /// five failures to be its own). So each factory gets its own namespace,
    /// through configuration, exactly as two deployments sharing a Redis would.
    /// </summary>
    protected virtual string JobKeyPrefix => _jobKeyPrefix;

    /// <summary>The same value, reachable by a sibling host on the same queue.</summary>
    internal string JobNamespace => JobKeyPrefix;

    /// <summary>
    /// Whether this host also PROCESSES what it enqueues. Production splits
    /// that — Hsm.Api enqueues, Hsm.Worker consumes — but a contract suite that
    /// waits on a job's observable outcome needs a consumer in the test
    /// process, so the suites that do (coms, docs) boot the worker's job
    /// processing INSIDE their host. Nothing about this arrangement exists in
    /// production code: it is composed here, out of the same registrations
    /// Hsm.Worker composes.
    /// </summary>
    protected virtual bool ConsumesJobs => false;

    protected sealed override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:HsmDb", ConnectionStringFor(DatabaseName));
        builder.UseSetting("Jobs:KeyPrefix", JobKeyPrefix);
        if (ConsumesJobs)
        {
            builder.ConfigureServices(services =>
            {
                services.AddHsmJobProcessing();

                // A job scope has no HttpContext — the consumer puts the
                // envelope's actor on AmbientPrincipal instead — so this host,
                // which production registers with HttpCurrentPrincipal alone,
                // needs to read whichever one applies. The test order is the
                // safe one: an HTTP request ALWAYS reads HttpCurrentPrincipal,
                // so an ambient actor can never leak into a route.
                services.AddScoped<HttpCurrentPrincipal>();
                services.AddScoped<ICurrentPrincipal>(sp =>
                    sp.GetRequiredService<IHttpContextAccessor>().HttpContext is null
                        ? sp.GetRequiredService<AmbientPrincipal>()
                        : sp.GetRequiredService<HttpCurrentPrincipal>());
            });
        }

        // HS256 keys must be at least 256 bits.
        builder.UseSetting("Auth:JwtAccessSecret", "contract_test_at_secret_0123456789abcdef");
        builder.UseSetting("Auth:JwtRefreshSecret", "contract_test_rt_secret_0123456789abcdef");
        builder.UseSetting("Auth:CsrfSecret", "contract_test_csrf_secret_0123456789abcdef");
        builder.UseSetting("Auth:Environment", "dev");
        ConfigureModule(builder);
    }

    /// <summary>Module-specific settings and service replacements.</summary>
    protected virtual void ConfigureModule(IWebHostBuilder builder)
    {
    }

    /// <summary>Applies this factory's module configuration to a sibling host.</summary>
    internal void ApplyModuleConfiguration(IWebHostBuilder builder) => ConfigureModule(builder);

    /// <summary>
    /// The dev container's Postgres by default, CI's via the same
    /// ConnectionStrings__HsmDb env override the integration tests use —
    /// always rewritten onto the factory's dedicated database.
    /// </summary>
    private static string ConnectionStringFor(string database)
    {
        var configured = Environment.GetEnvironmentVariable("ConnectionStrings__HsmDb")
            ?? "Host=postgres;Port=5432;Username=postgres;Password=postgres;Database=hsm";
        var withoutDatabase = configured
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => !part.StartsWith("Database=", StringComparison.OrdinalIgnoreCase));
        return string.Join(';', withoutDatabase.Append($"Database={database}"));
    }

    /// <summary>Recreates the dedicated database once per test run.</summary>
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
                await db.Database.EnsureCreatedAsync();
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
    /// cookie behavior is part of the contract under test.</summary>
    public virtual HttpClient CreateApiClient() => CreateClient(new WebApplicationFactoryClientOptions
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

/// <summary>
/// The REST door. Every frozen-contract suite boots <c>Hsm.Api</c> — the host
/// that actually serves /v1 and /fhir after the Task 17 split — so the frozen
/// contract is asserted against the artifact that ships it, and the
/// route-closure test diffs Hsm.Api's endpoint set against the snapshot.
/// </summary>
public abstract class ContractApiFactory : ContractHostFactory<Hsm.Api.Program>
{
}

/// <summary>
/// The staff door. Shell suites boot <c>Hsm.Web</c> — Blazor, screens, UI
/// services, no REST — and reach the API surface through a SIDECAR Hsm.Api
/// host on the same database, wired behind one client that routes by path.
///
/// That is not a convenience: it is the deployment, in miniature. A browser
/// signed in at the shell and an integration calling /v1 are two hosts sharing
/// one database and one set of JWT secrets, and the shell suites assert
/// exactly the properties that arrangement has to keep — sign in through
/// either door and the other recognizes you; provision an integration account
/// on a screen and its token works at the API. Serving both from one process
/// would prove nothing about the split this task exists to make.
/// </summary>
public abstract class ContractShellFactory : ContractHostFactory<Hsm.Web.Program>
{
    private readonly ApiSurfaceFactory _apiSurface;

    protected ContractShellFactory() => _apiSurface = new ApiSurfaceFactory(this);

    /// <summary>
    /// A client onto BOTH doors, routed by path the way a deployment's reverse
    /// proxy routes: /v1 and /fhir reach the Hsm.Api sidecar, everything else
    /// reaches this Blazor host. No redirect following and no cookie container,
    /// same as the single-host client — cookie behavior is under test.
    /// </summary>
    public override HttpClient CreateApiClient() =>
        new(new SurfaceRouter(_apiSurface.Server.CreateHandler(), Server.CreateHandler()))
        {
            BaseAddress = new Uri("http://localhost"),
        };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _apiSurface.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>The REST door, on the shell factory's database and settings.</summary>
    private sealed class ApiSurfaceFactory(ContractShellFactory shell) : ContractHostFactory<Hsm.Api.Program>
    {
        protected override string DatabaseName => shell.Database;

        /// <summary>One deployment: the sidecar enqueues where the shell does.</summary>
        protected override string JobKeyPrefix => shell.JobNamespace;

        protected override void ConfigureModule(IWebHostBuilder builder) =>
            shell.ApplyModuleConfiguration(builder);
    }

    /// <summary>Path-prefix routing across the two in-memory hosts.</summary>
    private sealed class SurfaceRouter : HttpMessageHandler
    {
        private readonly HttpMessageInvoker _api;
        private readonly HttpMessageInvoker _shell;

        public SurfaceRouter(HttpMessageHandler api, HttpMessageHandler shell)
        {
            _api = new HttpMessageInvoker(api);
            _shell = new HttpMessageInvoker(shell);
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? "/";
            var target = path.StartsWith("/v1", StringComparison.Ordinal)
                || path.StartsWith("/fhir", StringComparison.Ordinal)
                    ? _api
                    : _shell;
            return target.SendAsync(request, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _api.Dispose();
                _shell.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
