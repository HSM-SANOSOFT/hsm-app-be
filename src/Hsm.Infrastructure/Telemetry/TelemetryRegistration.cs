using System.Diagnostics;
using Hsm.Application.Abstractions.Behaviors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Hsm.Infrastructure.Telemetry;

/// <summary>
/// Which exporters one signal (traces, metrics, or logs) writes to. Plain
/// bools rather than a <c>[Flags]</c> enum so both the config parsing and the
/// test suite read as a straight set-membership check.
/// </summary>
public sealed record SignalExporters(bool Console, bool File, bool Otlp);

/// <summary>
/// The decision <see cref="TelemetryRegistration.AddHsmTelemetry"/> made from
/// <c>Telemetry:*</c> configuration, registered as a singleton so it can be
/// inspected without reaching into the OpenTelemetry SDK's own provider
/// internals (which are not a public contract and are not worth pinning a
/// test to). This is the seam <c>TelemetryConfigurationTests</c> reads: "did
/// console-only configuration actually resolve to no OTLP exporter anywhere,"
/// asked directly, in one property read, rather than by reflecting into a
/// built <see cref="TracerProvider"/>.
/// </summary>
public sealed record TelemetryOptions(
    string ServiceName,
    SignalExporters Traces,
    SignalExporters Metrics,
    SignalExporters Logs,
    string? OtlpEndpoint,
    string FileDirectory);

/// <summary>
/// One telemetry registration, shared by <c>Hsm.Api</c>, <c>Hsm.Web</c>, and
/// <c>Hsm.Worker</c> (rewrite plan U10, Task 23). Before this task, all three
/// hosts carried an identical <c>AddOpenTelemetry()</c> block that always
/// exported OTLP to whatever the collector's config pointed at — the
/// collector was the switchboard. Now the app decides: <c>Telemetry:Exporters</c>
/// (comma-separated <c>console</c> | <c>file</c> | <c>otlp</c>) is the default
/// exporter list for every signal, and <c>Telemetry:Traces|Metrics|Logs:Exporters</c>
/// overrides it per signal. The collector config
/// (<c>docker/otel/otel-collector.yaml</c>) goes back to being a passthrough:
/// OTLP receivers and a debug exporter, nothing that picks a destination.
/// </summary>
public static class TelemetryRegistration
{
    private static readonly string[] KnownExporters = ["console", "file", "otlp"];

    /// <summary>
    /// Wires resource, tracing, metrics, and logging from configuration, and
    /// registers the resolved <see cref="TelemetryOptions"/> so callers (and
    /// tests) can see what was decided.
    ///
    /// <para><b>Endpoint required, but only as a configuration gate.</b> If
    /// any signal selects <c>otlp</c>, <c>Telemetry:Otlp:Endpoint</c> must be
    /// set — this throws <see cref="InvalidOperationException"/> here, before
    /// the host finishes building, rather than silently falling back to a
    /// default endpoint nobody configured. That is a DIFFERENT failure from a
    /// collector that is merely unreachable once the host is running: OTel's
    /// own OTLP exporter already buffers and drops on export failure without
    /// touching availability (plan U10's property, unchanged) — a stopped
    /// collector must never stop this host from starting or serving. Only the
    /// "otlp selected, no endpoint configured" case — a config mistake, not a
    /// runtime condition — fails fast.</para>
    ///
    /// <para><b>No ASP.NET Core instrumentation here, on purpose.</b>
    /// <c>Hsm.Worker</c> has no HTTP surface and runs on the plain (non-ASP.NET)
    /// runtime image (<c>docker/worker.Dockerfile</c> — <c>mcr.microsoft.com/
    /// dotnet/runtime</c>, not <c>.../aspnet</c>). The
    /// <c>OpenTelemetry.Instrumentation.AspNetCore</c> package carries a
    /// <c>Microsoft.AspNetCore.App</c> <c>FrameworkReference</c> with it —
    /// referencing it from this shared assembly would push that framework
    /// dependency onto every consumer's <c>runtimeconfig.json</c>, including
    /// Hsm.Worker's, and its container ships no such framework: the host would
    /// fail to start. <c>Hsm.Api</c> and <c>Hsm.Web</c> already carry that
    /// package themselves (they are ASP.NET Core hosts) and layer the
    /// instrumentation on top of this registration via
    /// <c>services.ConfigureOpenTelemetryTracerProvider</c> /
    /// <c>ConfigureOpenTelemetryMeterProvider</c> — the same supported
    /// after-the-fact composition <c>Hsm.Web</c> already used for its circuit
    /// meter.</para>
    /// </summary>
    public static IHostApplicationBuilder AddHsmTelemetry(this IHostApplicationBuilder builder, string serviceName)
    {
        var configuration = builder.Configuration;

        var defaultTokens = ParseExporterTokens(configuration["Telemetry:Exporters"]);
        var tracesExporters = ResolveSignal(configuration, "Traces", defaultTokens);
        var metricsExporters = ResolveSignal(configuration, "Metrics", defaultTokens);
        var logsExporters = ResolveSignal(configuration, "Logs", defaultTokens);

        var otlpEndpoint = configuration["Telemetry:Otlp:Endpoint"];
        if ((tracesExporters.Otlp || metricsExporters.Otlp || logsExporters.Otlp)
            && string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            throw new InvalidOperationException(
                "Telemetry: the 'otlp' exporter is selected (Telemetry:Exporters, or a Telemetry:Traces|Metrics|"
                + "Logs:Exporters override) but Telemetry:Otlp:Endpoint is not configured. Set "
                + "Telemetry:Otlp:Endpoint, or remove 'otlp' from the exporter list. (A collector that is merely "
                + "unreachable once the host is running is a different, harmless condition — exports are dropped "
                + "and the host still starts; only a missing endpoint for a selected exporter is a configuration "
                + "mistake.)");
        }

        var fileDirectory = configuration["Telemetry:File:Directory"] ?? "telemetry";
        var options = new TelemetryOptions(
            serviceName, tracesExporters, metricsExporters, logsExporters, otlpEndpoint, fileDirectory);
        builder.Services.AddSingleton(options);

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing =>
            {
                // Every dispatched request gets a span here (RequestActivity /
                // TelemetryBehavior, plan U10) — the union across all three
                // hosts, since the CQRS pipeline runs in each of them
                // (including Hsm.Worker, dispatching a queued command).
                tracing.AddSource(RequestActivity.SourceName);
                tracing.AddHttpClientInstrumentation();
                tracing.AddNpgsql();
                ConfigureTraceExporters(tracing, tracesExporters, options);
            })
            .WithMetrics(metrics =>
            {
                metrics.AddHttpClientInstrumentation();
                metrics.AddRuntimeInstrumentation();
                metrics.AddNpgsqlInstrumentation();
                ConfigureMetricExporters(metrics, metricsExporters, options);
            })
            .WithLogging(logging => ConfigureLogExporters(logging, logsExporters, options));

        return builder;
    }

    private static SignalExporters ResolveSignal(
        IConfiguration configuration, string signal, IReadOnlySet<string> defaultTokens)
    {
        var raw = configuration[$"Telemetry:{signal}:Exporters"];
        var tokens = string.IsNullOrWhiteSpace(raw) ? defaultTokens : ParseExporterTokens(raw);
        return new SignalExporters(
            tokens.Contains("console"), tokens.Contains("file"), tokens.Contains("otlp"));
    }

    /// <summary>Console-only is the safe default: no network dependency, so a
    /// bare host — a unit test, a `dotnet run` with no Telemetry section at
    /// all — never throws and never reaches out. Every real deployment
    /// (appsettings.Development.json, secrets.env.template) sets
    /// Telemetry:Exporters explicitly.</summary>
    private static HashSet<string> ParseExporterTokens(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "console" };
        }

        var tokens = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var set = new HashSet<string>(tokens, StringComparer.OrdinalIgnoreCase);
        var unknown = set.Where(t => !KnownExporters.Contains(t, StringComparer.OrdinalIgnoreCase)).ToList();
        if (unknown.Count > 0)
        {
            throw new InvalidOperationException(
                $"Telemetry: unknown exporter(s) '{string.Join(", ", unknown)}'. Valid exporters: "
                + $"{string.Join(", ", KnownExporters)} (comma-separated).");
        }

        return set;
    }

    private static void ConfigureTraceExporters(
        TracerProviderBuilder tracing, SignalExporters exporters, TelemetryOptions options)
    {
        if (exporters.Console)
        {
            tracing.AddProcessor(new SimpleActivityExportProcessor(TextLineExporter<Activity>.ToConsole()));
        }

        if (exporters.Otlp)
        {
            tracing.AddOtlpExporter(otlp =>
            {
                otlp.Endpoint = new Uri(options.OtlpEndpoint!);
                otlp.Protocol = OtlpExportProtocol.HttpProtobuf;
            });
        }

        if (exporters.File)
        {
            tracing.AddProcessor(new SimpleActivityExportProcessor(
                TextLineExporter<Activity>.ToFile(FilePathFor(options, "traces"))));
        }
    }

    private static void ConfigureMetricExporters(
        MeterProviderBuilder metrics, SignalExporters exporters, TelemetryOptions options)
    {
        if (exporters.Console)
        {
            metrics.AddReader(new PeriodicExportingMetricReader(TextLineExporter<Metric>.ToConsole()));
        }

        if (exporters.Otlp)
        {
            metrics.AddOtlpExporter(otlp =>
            {
                otlp.Endpoint = new Uri(options.OtlpEndpoint!);
                otlp.Protocol = OtlpExportProtocol.HttpProtobuf;
            });
        }

        if (exporters.File)
        {
            metrics.AddReader(new PeriodicExportingMetricReader(
                TextLineExporter<Metric>.ToFile(FilePathFor(options, "metrics"))));
        }
    }

    private static void ConfigureLogExporters(
        LoggerProviderBuilder logging, SignalExporters exporters, TelemetryOptions options)
    {
        if (exporters.Console)
        {
            logging.AddProcessor(new SimpleLogRecordExportProcessor(TextLineExporter<LogRecord>.ToConsole()));
        }

        if (exporters.Otlp)
        {
            logging.AddOtlpExporter(otlp =>
            {
                otlp.Endpoint = new Uri(options.OtlpEndpoint!);
                otlp.Protocol = OtlpExportProtocol.HttpProtobuf;
            });
        }

        if (exporters.File)
        {
            logging.AddProcessor(new SimpleLogRecordExportProcessor(
                TextLineExporter<LogRecord>.ToFile(FilePathFor(options, "logs"))));
        }
    }

    private static string FilePathFor(TelemetryOptions options, string signal) =>
        Path.Combine(options.FileDirectory, $"{options.ServiceName}-{signal}.log");
}

/// <summary>
/// The <c>console</c> and <c>file</c> destinations, both: one line per item,
/// via <c>ToString()</c>, written to a <see cref="TextWriter"/> — stdout for
/// <c>console</c>, an append-mode file for <c>file</c>. Neither is an official
/// OpenTelemetry .NET exporter package (the project ships a console exporter,
/// but not a file one; this keeps the two symmetric rather than pulling in a
/// dependency for one and hand-rolling the other). Both are "read it yourself"
/// destinations, not a structured format other tooling consumes — anyone
/// wanting durable, structured telemetry uses <c>otlp</c>. Export failure here
/// follows the same rule as everywhere else (plan U10): caught and reported to
/// the SDK as a failed export, never thrown into the app.
/// </summary>
internal sealed class TextLineExporter<T> : BaseExporter<T>
    where T : class
{
    private readonly TextWriter _writer;
    private readonly bool _ownsWriter;

    private TextLineExporter(TextWriter writer, bool ownsWriter)
    {
        _writer = writer;
        _ownsWriter = ownsWriter;
    }

    public static TextLineExporter<T> ToConsole() => new(Console.Out, ownsWriter: false);

    public static TextLineExporter<T> ToFile(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var writer = new StreamWriter(new FileStream(fullPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        {
            AutoFlush = true,
        };
        return new TextLineExporter<T>(writer, ownsWriter: true);
    }

    public override ExportResult Export(in Batch<T> batch)
    {
        try
        {
            foreach (var item in batch)
            {
                _writer.WriteLine(FormatLine(item));
            }

            return ExportResult.Success;
        }
        catch (IOException)
        {
            // Export failure must never affect the host (U10): report it to
            // the SDK as a failed export, do not let it escape into the app.
            return ExportResult.Failure;
        }
    }

    /// <summary>
    /// <c>Activity</c>/<c>Metric</c>/<c>LogRecord</c> do not override
    /// <c>ToString()</c> with anything useful (it is the CLR default, the type
    /// name) — this pulls the handful of fields a human actually wants to see
    /// locally out of each of the three signal types this exporter is ever
    /// instantiated for.
    /// </summary>
    private static string FormatLine(T item) => item switch
    {
        Activity activity =>
            $"{activity.StartTimeUtc:O} trace_id={activity.TraceId} span_id={activity.SpanId} "
            + $"name={activity.DisplayName} status={activity.Status} duration_ms={activity.Duration.TotalMilliseconds}",
        Metric metric =>
            $"{DateTimeOffset.UtcNow:O} metric={metric.Name} unit={metric.Unit} type={metric.MetricType}",
        LogRecord log =>
            $"{log.Timestamp:O} level={log.LogLevel} category={log.CategoryName} "
            + $"message={log.FormattedMessage ?? log.Body}",
        _ => item.ToString() ?? string.Empty,
    };

    protected override bool OnForceFlush(int timeoutMilliseconds)
    {
        _writer.Flush();
        return true;
    }

    protected override bool OnShutdown(int timeoutMilliseconds)
    {
        _writer.Flush();
        return true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _ownsWriter)
        {
            _writer.Dispose();
        }

        base.Dispose(disposing);
    }
}
