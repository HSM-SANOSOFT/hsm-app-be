using System.Threading.RateLimiting;
using Hsm.Application.System;
using Hsm.Contracts.Ui;
using Hsm.Infrastructure;
using Hsm.Web.Api;
using Hsm.Web.Auth;
using Hsm.Web.Components;
using Hsm.Web.Coms;
using Hsm.Web.Docs;
using Hsm.Web.Services;
using Hsm.Web.Settings;
using Hsm.Web.Telemetry;
using Hsm.Web.Templates;
using Hsm.Web.Users;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.RateLimiting;
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

// Store-port adapters (EF Core/Npgsql, S3, Meilisearch, Redis cache) bound
// from configuration (plan U9).
builder.Services.AddHsmInfrastructure(builder.Configuration);

// Application handlers.
builder.Services.AddScoped<GetSystemStatusHandler>();

// UI services: interfaces declared in Hsm.Contracts, implemented by this
// host with in-process handler calls (client-isolation boundary, plan U8).
builder.Services.AddScoped<ISystemStatusUiService, SystemStatusUiService>();

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
    if (!CsrfProtection.ShouldSkip(ctx)
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

app.UseAntiforgery();

app.MapStaticAssets();
app.MapAuthEndpoints();
app.MapUserEndpoints();
app.MapSettingsEndpoints();
app.MapTemplateEndpoints();
app.MapComsEndpoints();
app.MapDocsEndpoints();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

// Exposes the entry point to WebApplicationFactory-based tests.
public partial class Program { }
