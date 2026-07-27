using Hsm.Application.System;
using Hsm.Contracts.Ui;
using Hsm.Infrastructure;
using Hsm.Web.Components;
using Hsm.Web.Services;
using Hsm.Web.Telemetry;
using Microsoft.AspNetCore.Components.Server.Circuits;
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

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

// Exposes the entry point to WebApplicationFactory-based tests.
public partial class Program { }
