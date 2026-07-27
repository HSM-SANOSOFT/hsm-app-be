using Hsm.Worker;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = Host.CreateApplicationBuilder(args);

// OpenTelemetry (plan U10): same posture as the web host — standard
// primitives, OTLP export, collector outage never affects the host.
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("hsm-worker"))
    .WithTracing(tracing => tracing
        .AddHttpClientInstrumentation()
        .AddNpgsql())
    .WithMetrics(metrics => metrics
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddNpgsqlInstrumentation())
    .WithLogging()
    .UseOtlpExporter(
        OtlpExportProtocol.HttpProtobuf,
        new Uri(builder.Configuration["Otlp:Endpoint"] ?? "http://localhost:4318"));

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
