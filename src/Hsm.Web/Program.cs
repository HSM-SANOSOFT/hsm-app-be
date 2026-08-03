using Hsm.Application.Abstractions;
using Hsm.Application.Auth;
using Hsm.Application.System.Queries.GetSystemStatus;
using Hsm.Contracts.Ui;
using Hsm.Infrastructure;
using Hsm.Infrastructure.Telemetry;
using Hsm.Web.Auth;
using Hsm.Web.Host;
using Hsm.Web.Services;
using Hsm.Web.Telemetry;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Components.Server.Circuits;
using MudBlazor.Services;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// OpenTelemetry (plan U10 + Task 23): destination is application
// configuration — AddHsmTelemetry is the one registration all three hosts
// share (see Hsm.Api/Program.cs and AddHsmTelemetry's doc comment for why
// ASP.NET Core instrumentation and this host's circuit meter are layered on
// afterward rather than living in the shared method).
builder.AddHsmTelemetry("hsm-web");
builder.Services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddAspNetCoreInstrumentation());
builder.Services.ConfigureOpenTelemetryMeterProvider(metrics =>
{
    metrics.AddAspNetCoreInstrumentation();
    metrics.AddMeter(CircuitMetrics.MeterName);
});

// Blazor circuit signals — without them a circuit leak looks like a memory leak.
builder.Services.AddSingleton<CircuitMetrics>();
builder.Services.AddScoped<CircuitHandler, MetricsCircuitHandler>();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMudServices();

// Shell authentication (plan U17): the access-token cookie — issued by this
// host's sign-in screen or by Hsm.Api's POST /v1/auth/login, identically,
// because both write Hsm.Contracts' AuthCookiePolicy — signs the Blazor shell
// in. Pages marked [Authorize] carry endpoint metadata, so anonymous visitors
// are challenged — redirected to /login — before anything renders.
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
// from configuration (plan U9). The same call Hsm.Api makes: two doors, one
// core — this host reaches the application in-process rather than over HTTP.
builder.Services.AddHsmInfrastructure(builder.Configuration);

// The request pipeline (dispatcher + telemetry/authorization/validation/
// transaction behaviors) and the actor it authorizes against. Authorization
// happens HERE and nowhere else: no UI service checks a role. This host has
// exactly one publisher of WHO is calling — ShellActor, which derives the
// actor from the circuit's authentication state and sets it on the scoped
// AmbientPrincipal, which IS this host's ICurrentPrincipal. Publishing
// nothing leaves the actor null, which the pipeline answers with 401, so a
// forgotten install fails closed. (The REST door reads its actor off
// HttpContext instead; the derivation both share is RequestActorFactory's,
// in Hsm.Application, so neither door can drift from the other.)
builder.Services.AddHsmPipeline();
builder.Services.AddScoped<AmbientPrincipal>();
builder.Services.AddScoped<ICurrentPrincipal>(sp => sp.GetRequiredService<AmbientPrincipal>());
builder.Services.AddScoped<RequestActorFactory>();
builder.Services.AddScoped<ShellActor>();

// Application handlers.
builder.Services.AddScoped<IRequestHandler<GetSystemStatusQuery, SystemStatusDto>, GetSystemStatusHandler>();

// UI services: interfaces declared in Hsm.Contracts, implemented by this
// host with in-process handler calls (client-isolation boundary, plan U8).
builder.Services.AddScoped<ISystemStatusUiService, SystemStatusUiService>();
builder.Services.AddScoped<ICurrentUserUiService, CurrentUserUiService>();

// Administrative screens (plan U18): each screen's data path goes through a
// contracts-declared UI service, which dispatches through the same pipeline
// the REST surface does — so the admin requirement is the request type's
// [RequireRole(Roles.Admin)], enforced once, whichever door called. Sign-in
// runs in static SSR and needs the live HttpContext to set the session
// cookies.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ISignInUiService, SignInUiService>();
builder.Services.AddScoped<IUsersAdminUiService, UsersAdminUiService>();
builder.Services.AddScoped<IIntegrationAccountsUiService, IntegrationAccountsUiService>();
builder.Services.AddScoped<ISettingsAdminUiService, SettingsAdminUiService>();
builder.Services.AddScoped<IDocumentsAdminUiService, DocumentsAdminUiService>();

// Cookie posture from configuration (frozen COOKIE_* envs). The names, paths,
// SameSite modes and lifetimes are AuthCookiePolicy's; only the posture is
// per-deployment. No CsrfSecret here: the frozen double-submit CSRF guards
// the REST surface, which this host no longer serves — Blazor form posts are
// guarded by UseAntiforgery below.
builder.Services.AddSingleton(new AuthWebOptions
{
    CookieSecure = builder.Configuration.GetValue("Auth:CookieSecure", defaultValue: false),
    CookieDomain = builder.Configuration["Auth:CookieDomain"],
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

// Shell session: authenticate from the access-token cookie, then enforce the
// [Authorize] endpoint metadata Blazor pages carry. Must precede antiforgery.
app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapStaticAssets();
// Routable pages live in this same host assembly (Option B, plan U8
// amendment): the client-isolation boundary is now held by a test, not by a
// separate assembly, so there is nothing additional to map here. Nothing else
// is mapped either — the /v1 and /fhir surfaces are Hsm.Api's, and this host
// answers a plain 404 for them.
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

namespace Hsm.Web
{
    /// <summary>
    /// The entry-point marker <c>WebApplicationFactory&lt;Hsm.Web.Program&gt;</c>
    /// resolves this assembly through. Both doors publish a <c>Program</c> in
    /// the global namespace (the compiler's, for top-level statements), so a
    /// test assembly that references both — the contract suites do, one door
    /// each — cannot say which it means. Naming them per host settles it.
    /// <c>WebApplicationFactory</c> only ever reads
    /// <c>typeof(TEntryPoint).Assembly</c>, so the real entry point above is
    /// what boots.
    /// </summary>
    public sealed class Program;
}
