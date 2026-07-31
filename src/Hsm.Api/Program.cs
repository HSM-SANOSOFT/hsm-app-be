using System.Threading.RateLimiting;
using Hsm.Api.Auth;
using Hsm.Api.Coms;
using Hsm.Api.Docs;
using Hsm.Api.Fhir;
using Hsm.Api.Health;
using Hsm.Api.Http;
using Hsm.Api.Settings;
using Hsm.Api.Templates;
using Hsm.Api.Users;
using Hsm.Application.Abstractions;
using Hsm.Application.Auth;
using Hsm.Infrastructure;
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
// boot or request handling (the exporter buffers and drops). The service name
// is what separates this door from hsm-web in every trace and metric.
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("hsm-api"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddNpgsql())
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddNpgsqlInstrumentation())
    .WithLogging()
    .UseOtlpExporter(
        OtlpExportProtocol.HttpProtobuf,
        new Uri(builder.Configuration["Otlp:Endpoint"] ?? "http://localhost:4318"));

// Store-port adapters (EF Core/Npgsql, S3, Meilisearch, Redis cache) bound
// from configuration (plan U9). The same call Hsm.Web makes: one core, two
// doors — this host owns no persistence of its own.
builder.Services.AddHsmInfrastructure(builder.Configuration);

// The request pipeline (dispatcher + telemetry/authorization/validation/
// transaction behaviors) and the actor it authorizes against. Authorization
// happens HERE and nowhere else: no endpoint checks a role. The edge only
// publishes WHO is calling, through RequestAuth.GateAsync, and
// HttpCurrentPrincipal reads it back. Publishing nothing leaves the actor
// null, which the pipeline answers with 401, so a forgotten install fails
// closed. RequestActorFactory is the shared derivation of onboarding state —
// Hsm.Application's, so both doors decide it identically.
builder.Services.AddHsmPipeline();
builder.Services.AddHttpContextAccessor();
// ONE source of who is calling, and it is the request. This host serves HTTP
// and nothing else: it enqueues background work into the shared queue
// namespace and never consumes it — Hsm.Worker does — so there is no scope
// here without an HttpContext and no second, ambient actor that could leak
// into a route.
builder.Services.AddScoped<ICurrentPrincipal, HttpCurrentPrincipal>();
builder.Services.AddScoped<RequestActorFactory>();

// Auth web surface (plan U12): cookie posture + CSRF from configuration. The
// cookie NAMES, paths, SameSite modes and lifetimes are Hsm.Contracts'
// AuthCookiePolicy, shared with Hsm.Web so one sign-in serves both doors;
// only the per-deployment posture is bound here.
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

// API failures render the frozen error envelope (scoped to /v1/*); the FHIR
// surface renders OperationOutcome through its own wrapper.
app.UseApiErrorEnvelope();

if (!app.Environment.IsDevelopment())
{
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseRateLimiter();

// CSRF double-submit (frozen csrf.util.ts): validates x-csrf-token on
// cookie-authenticated browser mutations; safe methods, bearer clients, and
// pre-session requests are skipped. Runs before endpoints, as the frozen
// middleware ran before guards. It lives on this door alone: the token is
// issued by GET /v1/auth/csrf and every route it protects is here, so the
// /_blazor exemption the combined host needed has no subject any more.
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

app.MapHealthEndpoints();
app.MapFhirEndpoints();
app.MapAuthEndpoints();
app.MapUserEndpoints();
app.MapSettingsEndpoints();
app.MapTemplateEndpoints();
app.MapComsEndpoints();
app.MapDocsEndpoints();

app.Run();

namespace Hsm.Api
{
    /// <summary>
    /// The entry-point marker <c>WebApplicationFactory&lt;Hsm.Api.Program&gt;</c>
    /// resolves this assembly through. It is named rather than being the
    /// top-level statements' own <c>Program</c> because <c>Hsm.Web</c> already
    /// publishes a global one, and a test assembly referencing both doors could
    /// not say which <c>Program</c> it meant. <c>WebApplicationFactory</c> only
    /// ever reads <c>typeof(TEntryPoint).Assembly</c>, so the real entry point
    /// above is what boots.
    /// </summary>
    public sealed class Program;
}
