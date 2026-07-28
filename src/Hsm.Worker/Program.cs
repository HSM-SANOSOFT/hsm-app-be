using Hsm.Infrastructure;
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

// Store-port adapters plus the communications dispatch surface (plan U14).
// AddHsmInfrastructure registers the ComsJobProcessor hosted service, so this
// host runs the same background send processing as Hsm.Web. With the default
// in-process channel adapter each process consumes only its own enqueues —
// the web host serves HTTP-observable dispatch; this host is the seat for a
// distributed-queue adapter should cross-process dispatch return (see
// ChannelComsDispatcher for the topology decision).
builder.Services.AddHsmInfrastructure(builder.Configuration);

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
