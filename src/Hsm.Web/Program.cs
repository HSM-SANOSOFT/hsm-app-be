using Hsm.Application.Abstractions;
using Hsm.Application.Auth;
using Hsm.Contracts.Ui;
using Hsm.Infrastructure;
using Hsm.Infrastructure.Identity;
using Hsm.Infrastructure.Telemetry;
using Hsm.Web.Auth;
using Hsm.Web.Host;
using Hsm.Web.Services;
using Hsm.Web.Telemetry;
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

// Shell authentication (plan U17): the ONE Identity session cookie — issued by
// this host's sign-in screen or by Hsm.Api's POST /api/v1/identity/login,
// interchangeably, because both hosts call the same registration and (in a
// real deployment) share one data-protection key ring. Pages marked
// [Authorize] carry endpoint metadata, so anonymous visitors are challenged
// before anything renders.
builder.Services.AddHsmIdentityAuthentication(builder.Configuration);
builder.Services.AddAuthorization();

// This door's answer to "not signed in" is a screen, not a status code — the
// opposite of Hsm.Api's, which is why the events are per host. The lower-case
// returnUrl is the shell's own convention (RedirectToLogin writes it, and
// ShellAuthenticationTests reads it back).
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/login";
    options.ReturnUrlParameter = "returnUrl";

    // "Signed in but not allowed here" must stay a 403. Redirecting an admin
    // screen's refusal to another page would render a 200 that looks like the
    // screen loaded.
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});

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

// Shell session: authenticate from the Identity session cookie, then enforce
// the [Authorize] endpoint metadata Blazor pages carry. Must precede
// antiforgery.
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
