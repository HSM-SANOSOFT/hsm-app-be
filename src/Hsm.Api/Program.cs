using System.Threading.RateLimiting;
using Hsm.Api;
using Hsm.Api.Auth;
using Hsm.Api.Documents;
using Hsm.Api.Emails;
using Hsm.Api.Errors;
using Hsm.Api.Fhir;
using Hsm.Api.Http;
using Hsm.Api.Settings;
using Hsm.Api.SystemStatus;
using Hsm.Api.Templates;
using Hsm.Api.Users;
using Hsm.Api.Webhooks;
using Hsm.Application.Abstractions;
using Hsm.Application.Auth;
using Hsm.Infrastructure;
using Hsm.Infrastructure.Telemetry;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

// The migrate step short-circuits before ANY host is built: `--migrate` runs
// migrations and exits, and normal boot never migrates. See MigrateCommand for
// why the two are separate invocations.
if (MigrateCommand.IsRequested(args))
{
    return await MigrateCommand.RunAsync(args);
}

var builder = WebApplication.CreateBuilder(args);

// OpenTelemetry (plan U10 + Task 23): destination is application
// configuration (Telemetry:Exporters / Telemetry:Traces|Metrics|Logs), not a
// property of the collector — AddHsmTelemetry is the one registration all
// three hosts share. Export failure still degrades observability, never
// availability (unchanged property, see AddHsmTelemetry's doc comment). ASP.NET
// Core instrumentation is layered on afterward because Hsm.Worker has no HTTP
// surface — see AddHsmTelemetry for why it cannot live in the shared method.
// The service name is what separates this door from hsm-web in every trace
// and metric.
builder.AddHsmTelemetry("hsm-api");
builder.Services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddAspNetCoreInstrumentation());
builder.Services.ConfigureOpenTelemetryMeterProvider(metrics => metrics.AddAspNetCoreInstrumentation());

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

// Liveness only, deliberately: no checks are registered, so /health answers
// 200 as long as the process can serve a request. It never probes Postgres
// or Redis — this is what a container orchestrator restarts the process on,
// and restarting a healthy API over a two-second database blip turns a brief
// degradation into an outage. Dependency state is reported, not acted on, by
// GET /api/v1/system/status (SystemEndpoints) instead.
builder.Services.AddHealthChecks();
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

// RFC 9457 for every failure on this door. traceId is attached HERE, once, so
// a mapping branch cannot ship without it — and the Activity id is preferred
// over TraceIdentifier because that is what a reader will find in the traces.
builder.Services.AddProblemDetails(options =>
    options.CustomizeProblemDetails = context =>
        context.ProblemDetails.Extensions["traceId"] =
            System.Diagnostics.Activity.Current?.Id ?? context.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<HsmExceptionHandler>();

// ThrowOnBadRequest defaults to true only in Development — in every other
// environment a minimal-API body-binding failure instead logs quietly and
// short-circuits with an empty, non-ProblemDetails 400, which would silently
// break the "every failure renders problem+json" invariant outside the
// environment the test host happens to run in. Pinned true everywhere so
// HsmExceptionHandler's BadHttpRequestException arm is reachable in every
// deployment, not just Development.
builder.Services.Configure<Microsoft.AspNetCore.Routing.RouteHandlerOptions>(
    options => options.ThrowOnBadRequest = true);

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
        // The rejection never reaches an endpoint, so nothing throws — the 429
        // problem is written here directly, in the same shape the handler
        // produces for TooManyRequestsException.
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.RequestServices
            .GetRequiredService<IProblemDetailsService>()
            .TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = context.HttpContext,
                ProblemDetails = new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = "Too Many Requests",
                },
            });
    };
});

var app = builder.Build();

// RFC 9457 problem+json for every failure on this door — the ONE place an
// exception becomes a response (HsmExceptionHandler). Placed first so
// everything downstream, including the antiforgery middleware Task 12 adds,
// renders through it.
app.UseExceptionHandler();

// Transitional: installs the actor for the reshaped /api routes while the old
// per-endpoint GateAsync still serves the un-reshaped /v1 ones. Task 12
// replaces this whole block with UseAuthentication() + UseHsmActor().
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/api")
        && RequestAuth.AccessToken(ctx) is not null)
    {
        var principal = await RequestAuth.AuthenticateAsync(ctx, TokenKind.Access);
        await RequestAuth.InstallActorAsync(ctx, principal);
    }

    await next();
});

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

app.MapHealthChecks("/health");
app.MapSystemEndpoints();
app.MapFhirEndpoints();
app.MapAuthEndpoints();
app.MapUserEndpoints();
app.MapSettingsEndpoints();
app.MapTemplateEndpoints();
app.MapEmailEndpoints();
app.MapWebhookEndpoints();
app.MapDocumentEndpoints();

app.Run();
return 0;

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
