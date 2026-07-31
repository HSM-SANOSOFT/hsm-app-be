using Hsm.Application.Abstractions;
using Hsm.Infrastructure;
using Hsm.Worker;
using Hsm.Worker.Scheduling;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = Host.CreateApplicationBuilder(args);

// OpenTelemetry (plan U10): same posture as the two web hosts — standard
// primitives, OTLP export, a collector outage never affects the host. The
// service name is what separates background work from request handling in
// every trace and metric, and it matters most here: a job's span is the only
// place its work is visible at all.
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

// Store-port adapters plus the job queue (plan U9/U14). The same call the two
// doors make: one core, three hosts — this one owns no persistence of its own
// either.
builder.Services.AddHsmInfrastructure(builder.Configuration);

// A queued command is dispatched, not invoked: telemetry, authorization
// against the actor that ENQUEUED it, validation — the same pipeline the HTTP
// request that queued it went through. That actor comes off the job envelope
// and onto AmbientPrincipal, which IS this host's ICurrentPrincipal: there is
// no request here to read one from, and a job carrying no actor authorizes as
// anonymous, which the pipeline refuses unless the command says otherwise.
// (RequestActorFactory — the two doors' shared derivation of onboarding state
// at sign-in — has no subject in this host: nothing on the job path resolves
// it, so it is not registered.)
builder.Services.AddHsmPipeline();
builder.Services.AddScoped<ICurrentPrincipal>(sp => sp.GetRequiredService<AmbientPrincipal>());

// THE consumer. Hsm.Api and Hsm.Web enqueue into the shared queue namespace
// and never drain it; this host drains it. Background work is therefore out of
// the request path, and an API restart cannot strand a job — what it enqueued
// is in the keys this host reads.
builder.Services.AddHsmJobProcessing();

// Recurring work, declared in one place and EMPTY on purpose: the frozen
// application had no cron, so there is nothing to carry over. A schedule fires
// once across N workers because each tick is claimed through Redis (see
// Scheduler); durable exactly-once scheduling is out of scope (see
// ScheduleRegistry).
var schedules = new ScheduleRegistry();

// schedules.Add(new ScheduledJob(
//     "coms.sweep-stuck-batches",
//     TimeSpan.FromMinutes(15),
//     (dispatcher, ct) => dispatcher.Send(new SweepStuckBatchesCommand(), ct)));

builder.Services.AddSingleton(schedules);
builder.Services.AddHostedService<Scheduler>();

var host = builder.Build();
host.Run();
