using System.Threading.RateLimiting;
using Hsm.Application.Abstractions;
using Hsm.Application.System.Queries.GetSystemStatus;
using Hsm.Contracts.Ui;
using Hsm.Infrastructure;
using Hsm.Web.Api;
using Hsm.Web.Auth;
using Hsm.Web.Coms;
using Hsm.Web.Docs;
using Hsm.Web.Fhir;
using Hsm.Web.Health;
using Hsm.Web.Host;
using Hsm.Web.Services;
using Hsm.Web.Settings;
using Hsm.Web.Telemetry;
using Hsm.Web.Templates;
using Hsm.Web.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.RateLimiting;
using MudBlazor.Services;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// OpenTelemetry (plan U10): standard primitives, off-the-shelf
// instrumentation, OTLP export to the collector. Export failure degrades
// observability, never availability — a stopped collector must not affect
// boot or request handling (the exporter buffers and drops).
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("hsm-web"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddNpgsql())
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddNpgsqlInstrumentation()
        .AddMeter(CircuitMetrics.MeterName))
    .WithLogging()
    .UseOtlpExporter(
        OtlpExportProtocol.HttpProtobuf,
        new Uri(builder.Configuration["Otlp:Endpoint"] ?? "http://localhost:4318"));

// Blazor circuit signals — without them a circuit leak looks like a memory leak.
builder.Services.AddSingleton<CircuitMetrics>();
builder.Services.AddScoped<CircuitHandler, MetricsCircuitHandler>();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMudServices();

// Shell authentication (plan U17): the same access-token cookie the REST
// surface issues signs the Blazor shell in. Pages marked [Authorize] carry
// endpoint metadata, so anonymous visitors are challenged — redirected to
// /login — before anything renders; the /v1 surface keeps its own frozen
// guard chain and is untouched by this scheme.
builder.Services
    .AddAuthentication(HsmCookieAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, HsmCookieAuthenticationHandler>(
        HsmCookieAuthenticationHandler.SchemeName, displayName: null, configureOptions: null);
builder.Services.AddAuthorization();

// Blazor-side auth state: the host hands the validated HttpContext principal
// to this provider at circuit start (and per statically rendered page).
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<HsmAuthenticationStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp =>
    sp.GetRequiredService<HsmAuthenticationStateProvider>());
builder.Services.AddScoped<IHostEnvironmentAuthenticationStateProvider>(sp =>
    sp.GetRequiredService<HsmAuthenticationStateProvider>());

// Store-port adapters (EF Core/Npgsql, S3, Meilisearch, Redis cache) bound
// from configuration (plan U9).
builder.Services.AddHsmInfrastructure(builder.Configuration);

// The request pipeline (dispatcher + telemetry/authorization/validation/
// transaction behaviors) and the actor it authorizes against. Both the REST
// surface (RequestAuth.GateAsync) and the Blazor surface (UiServiceGate)
// publish the authenticated principal into the same request-scoped
// AmbientPrincipal, so a command is gated identically from either edge.
builder.Services.AddHsmPipeline();
builder.Services.AddScoped<AmbientPrincipal>();
builder.Services.AddScoped<ICurrentPrincipal>(sp => sp.GetRequiredService<AmbientPrincipal>());

// Application handlers.
builder.Services.AddScoped<IRequestHandler<GetSystemStatusQuery, SystemStatusDto>, GetSystemStatusHandler>();

// UI services: interfaces declared in Hsm.Contracts, implemented by this
// host with in-process handler calls (client-isolation boundary, plan U8).
builder.Services.AddScoped<ISystemStatusUiService, SystemStatusUiService>();
builder.Services.AddScoped<ICurrentUserUiService, CurrentUserUiService>();

// Administrative screens (plan U18): each screen's data path goes through a
// contracts-declared UI service; the admin-gated ones re-check the role from
// the authenticated principal because no endpoint guard fronts an in-process
// call. Sign-in runs in static SSR and needs the live HttpContext to set the
// session cookies.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<UiServiceGate>();
builder.Services.AddScoped<ISignInUiService, SignInUiService>();
builder.Services.AddScoped<IUsersAdminUiService, UsersAdminUiService>();
builder.Services.AddScoped<IIntegrationAccountsUiService, IntegrationAccountsUiService>();
builder.Services.AddScoped<ISettingsAdminUiService, SettingsAdminUiService>();
builder.Services.AddScoped<IDocumentsAdminUiService, DocumentsAdminUiService>();

// Auth web surface (plan U12): cookie posture + CSRF from configuration.
builder.Services.AddSingleton(new AuthWebOptions
{
    CookieSecure = builder.Configuration.GetValue("Auth:CookieSecure", defaultValue: false),
    CookieDomain = builder.Configuration["Auth:CookieDomain"],
    CsrfSecret = builder.Configuration["Auth:CsrfSecret"] ?? string.Empty,
});
builder.Services.AddSingleton<CsrfProtection>();

// Frozen per-IP throttle on the account-recovery routes: 10 per 60s per
// route (auth.controller.ts @Throttle long).
builder.Services.AddRateLimiter(limiter =>
{
    limiter.AddPolicy(AuthEndpoints.RecoveryRateLimitPolicy, ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            $"{ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown"}:{ctx.Request.Path}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromSeconds(60),
                QueueLimit = 0,
            }));
    limiter.OnRejected = async (context, cancellationToken) =>
    {
        // The frozen ThrottlerException envelope carried only the
        // status-mapped code (its payload was a bare string).
        await ApiEnvelope.WriteErrorAsync(
            context.HttpContext,
            StatusCodes.Status429TooManyRequests,
            []);
    };
});

var app = builder.Build();

// API failures render the frozen error envelope (scoped to /v1/*).
app.UseApiErrorEnvelope();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseRateLimiter();

// CSRF double-submit (frozen csrf.util.ts): validates x-csrf-token on
// cookie-authenticated browser mutations; safe methods, bearer clients, and
// pre-session requests are skipped. Runs before endpoints, as the frozen
// middleware ran before guards.
app.Use(async (ctx, next) =>
{
    // The Blazor transport (/_blazor negotiate POSTs) is outside the frozen
    // CSRF surface: the client never carries the x-csrf-token header, and
    // page-level form posts are protected by antiforgery below.
    if (!ctx.Request.Path.StartsWithSegments("/_blazor")
        && !CsrfProtection.ShouldSkip(ctx)
        && !ctx.RequestServices.GetRequiredService<CsrfProtection>().Validate(ctx))
    {
        // The frozen failure surfaced from the express layer, outside the
        // response envelope.
        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
        ctx.Response.ContentType = "text/plain";
        await ctx.Response.WriteAsync("ForbiddenError: invalid csrf token");
        return;
    }

    await next();
});

// Shell session: authenticate from the access-token cookie, then enforce the
// [Authorize] endpoint metadata Blazor pages carry. Must precede antiforgery.
app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapHealthEndpoints();
app.MapFhirEndpoints();
app.MapAuthEndpoints();
app.MapUserEndpoints();
app.MapSettingsEndpoints();
app.MapTemplateEndpoints();
app.MapComsEndpoints();
app.MapDocsEndpoints();
// Routable pages live in the component library; the host only maps them.
app.MapRazorComponents<App>()
    .AddAdditionalAssemblies(typeof(Hsm.Web.Components.Routes).Assembly)
    .AddInteractiveServerRenderMode();

app.Run();

// Exposes the entry point to WebApplicationFactory-based tests.
public partial class Program { }
