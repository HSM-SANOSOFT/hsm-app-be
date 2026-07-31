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

// Store-port adapters plus the durable job queue (plan U14/Task 19).
// AddHsmInfrastructure registers the PRODUCING side of the queue only: this
// host does not consume yet. Task 20 turns it into the consumer — the
// consume loop, the delayed pump, and AmbientPrincipal as this host's
// ICurrentPrincipal — at which point the interim consumer inside Hsm.Api
// goes away and jobs are processed here, out of the request path.
builder.Services.AddHsmInfrastructure(builder.Configuration);

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
