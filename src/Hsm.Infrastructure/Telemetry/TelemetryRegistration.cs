using System.Diagnostics;
using System.Globalization;
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
    /// <para><b>The base endpoint gets the signal path appended explicitly.</b>
    /// <c>OtlpExporterOptions.Endpoint</c>, once assigned programmatically,
    /// stops the SDK from appending <c>/v1/traces</c> / <c>/v1/metrics</c> /
    /// <c>/v1/logs</c> itself — that auto-append only fires when the option is
    /// left at its own default. Setting <c>Endpoint</c> to the bare configured
    /// base (as the pre-Task-23 code effectively risked once it stopped going
    /// through <c>UseOtlpExporter(protocol, baseUri)</c>'s own per-signal
    /// wiring) would silently POST every signal to the bare base path instead
    /// — a 404 the collector answers and the exporter swallows exactly like
    /// any other export failure, so it would never surface as an error
    /// anywhere. <see cref="OtlpSignalEndpoint"/> appends the correct path
    /// itself, per signal, so this cannot regress silently again.</para>
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

    /// <summary>
    /// Appends the OTLP/HTTP signal path to the configured base endpoint.
    /// <c>OtlpExporterOptions.Endpoint</c>, once assigned, is used EXACTLY as
    /// given — the SDK only appends <c>/v1/{signal}</c> itself when the caller
    /// never touches <c>Endpoint</c> (i.e. relies on its own per-signal
    /// default). Any code that assigns <c>Endpoint</c> to a shared base value
    /// must therefore build the full per-signal URL itself, or every signal
    /// silently posts to the bare base path (a 404 the exporter treats as an
    /// ordinary export failure — nothing surfaces).
    /// </summary>
    private static Uri OtlpSignalEndpoint(string baseEndpoint, string signalPath) =>
        new($"{baseEndpoint.TrimEnd('/')}/{signalPath}", UriKind.Absolute);

    private static void ConfigureTraceExporters(
        TracerProviderBuilder tracing, SignalExporters exporters, TelemetryOptions options)
    {
        if (exporters.Console)
        {
            tracing.AddConsoleExporter();
        }

        if (exporters.Otlp)
        {
            tracing.AddOtlpExporter(otlp =>
            {
                otlp.Endpoint = OtlpSignalEndpoint(options.OtlpEndpoint!, "v1/traces");
                otlp.Protocol = OtlpExportProtocol.HttpProtobuf;
            });
        }

        if (exporters.File)
        {
            // Batch, not Simple: a request thread must never block on a
            // synchronous file write (plan U10's spirit extends to the local
            // destinations too, not only network ones).
            tracing.AddProcessor(new BatchActivityExportProcessor(
                FileLineExporter<Activity>.Open(FilePathFor(options, "traces"))));
        }
    }

    private static void ConfigureMetricExporters(
        MeterProviderBuilder metrics, SignalExporters exporters, TelemetryOptions options)
    {
        if (exporters.Console)
        {
            metrics.AddConsoleExporter();
        }

        if (exporters.Otlp)
        {
            metrics.AddOtlpExporter(otlp =>
            {
                otlp.Endpoint = OtlpSignalEndpoint(options.OtlpEndpoint!, "v1/metrics");
                otlp.Protocol = OtlpExportProtocol.HttpProtobuf;
            });
        }

        if (exporters.File)
        {
            // Metric export already runs off the request thread (a periodic
            // reader on its own timer), so there is no Simple/Batch choice to
            // make here the way there is for traces and logs.
            metrics.AddReader(new PeriodicExportingMetricReader(
                FileLineExporter<Metric>.Open(FilePathFor(options, "metrics"))));
        }
    }

    private static void ConfigureLogExporters(
        LoggerProviderBuilder logging, SignalExporters exporters, TelemetryOptions options)
    {
        if (exporters.Console)
        {
            logging.AddConsoleExporter();
        }

        if (exporters.Otlp)
        {
            logging.AddOtlpExporter(otlp =>
            {
                otlp.Endpoint = OtlpSignalEndpoint(options.OtlpEndpoint!, "v1/logs");
                otlp.Protocol = OtlpExportProtocol.HttpProtobuf;
            });
        }

        if (exporters.File)
        {
            logging.AddProcessor(new BatchLogRecordExportProcessor(
                FileLineExporter<LogRecord>.Open(FilePathFor(options, "logs"))));
        }
    }

    private static string FilePathFor(TelemetryOptions options, string signal) =>
        Path.Combine(options.FileDirectory, $"{options.ServiceName}-{signal}.log");
}

/// <summary>
/// The <c>file</c> destination: one line per item, via a small per-type
/// formatter (see <see cref="FormatLine"/>), appended to a file. No official
/// OpenTelemetry .NET file exporter exists (the project ships console and
/// OTLP only) — this is this repo's minimal stand-in. It is a "read it
/// yourself locally" sink, not a structured format other tooling is meant to
/// consume; anyone wanting durable, structured telemetry uses <c>otlp</c>.
/// </summary>
internal sealed class FileLineExporter<T> : BaseExporter<T>
    where T : class
{
    private readonly StreamWriter _writer;
    private bool _disposed;

    private FileLineExporter(StreamWriter writer) => _writer = writer;

    /// <summary>
    /// Opens the destination file, creating its directory if needed. Export
    /// failure — including AT SETUP, here — must never affect startup (U10):
    /// an uncreatable path (bad permissions, a missing drive, a read-only
    /// container filesystem) degrades this signal's file export to a no-op
    /// for the run instead of taking the host down. The failure is reported
    /// to stderr, once, at startup, since there is no ILogger available this
    /// early in the builder pipeline.
    /// </summary>
    public static BaseExporter<T> Open(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var writer = new StreamWriter(
                new FileStream(fullPath, FileMode.Append, FileAccess.Write, FileShare.Read))
            {
                AutoFlush = false,
            };
            return new FileLineExporter<T>(writer);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Telemetry: could not open the file destination '{path}' ({ex.GetType().Name}: {ex.Message}); "
                + "file export for this signal is disabled for this run rather than failing startup (U10).");
            return new NoopExporter<T>();
        }
    }

    public override ExportResult Export(in Batch<T> batch)
    {
        if (_disposed)
        {
            return ExportResult.Failure;
        }

        try
        {
            foreach (var item in batch)
            {
                _writer.WriteLine(FormatLine(item));
            }

            // Flushed once per BATCH here, not per item (that was the
            // AutoFlush=true problem I5 flagged), and not deferred to
            // OnForceFlush/OnShutdown alone: BatchActivityExportProcessor's
            // own ForceFlush drains its queue by calling this method, but
            // does not reliably also invoke the underlying exporter's
            // OnForceFlush — confirmed empirically (a diagnostic build showed
            // Export() running on every ForceFlush call while OnForceFlush
            // never fired). Flushing here is what actually guarantees a
            // batch's bytes reach disk, on every path that calls Export
            // (scheduled delay, ForceFlush, or the drain before shutdown) —
            // OnForceFlush/OnShutdown below still flush too, for defense in
            // depth, but are not load-bearing.
            _writer.Flush();

            return ExportResult.Success;
        }
        catch (Exception)
        {
            // Export failure must never affect the host (U10): report it to
            // the SDK as a failed export, do not let it escape into the app.
            // Broad on purpose — disk writes fail in more ways than IOException
            // (a writer raced with Dispose, permissions changing mid-run, ...).
            return ExportResult.Failure;
        }
    }

    /// <summary>
    /// <c>Activity</c>/<c>Metric</c>/<c>LogRecord</c> do not override
    /// <c>ToString()</c> with anything useful (it is the CLR default, the type
    /// name) — this pulls the fields a human actually wants to see locally out
    /// of each of the three signal types this exporter is ever instantiated
    /// for, including tags/attributes and (for metrics) the actual recorded
    /// values, not just the instrument's name.
    /// </summary>
    private static string FormatLine(T item) => item switch
    {
        Activity activity =>
            $"{activity.StartTimeUtc:O} trace_id={activity.TraceId} span_id={activity.SpanId} "
            + $"name={activity.DisplayName} status={activity.Status} duration_ms={activity.Duration.TotalMilliseconds} "
            + $"tags={{{FormatTags(activity.TagObjects)}}}",
        Metric metric => FormatMetric(metric),
        LogRecord log =>
            $"{log.Timestamp:O} level={log.LogLevel} category={log.CategoryName} "
            + $"message={log.FormattedMessage ?? log.Body} attributes={{{FormatTags(log.Attributes)}}}",
        _ => item.ToString() ?? string.Empty,
    };

    private static string FormatMetric(Metric metric)
    {
        var points = new List<string>();
        foreach (var point in metric.GetMetricPoints())
        {
            var tags = FormatTags(point.Tags);
            var value = FormatMetricValue(metric.MetricType, point);
            points.Add(tags.Length > 0 ? $"{{{tags}}}={value}" : value);
        }

        var pointsText = points.Count > 0 ? string.Join(' ', points) : "(no data points)";
        return $"{DateTimeOffset.UtcNow:O} metric={metric.Name} unit={metric.Unit} type={metric.MetricType} {pointsText}";
    }

    private static string FormatMetricValue(MetricType type, MetricPoint point) => type switch
    {
        MetricType.LongSum or MetricType.LongSumNonMonotonic =>
            point.GetSumLong().ToString(CultureInfo.InvariantCulture),
        MetricType.DoubleSum or MetricType.DoubleSumNonMonotonic =>
            point.GetSumDouble().ToString("G", CultureInfo.InvariantCulture),
        MetricType.LongGauge => point.GetGaugeLastValueLong().ToString(CultureInfo.InvariantCulture),
        MetricType.DoubleGauge => point.GetGaugeLastValueDouble().ToString("G", CultureInfo.InvariantCulture),
        MetricType.Histogram or MetricType.ExponentialHistogram =>
            $"count={point.GetHistogramCount().ToString(CultureInfo.InvariantCulture)} "
            + $"sum={point.GetHistogramSum().ToString("G", CultureInfo.InvariantCulture)}",
        _ => "(unsupported metric type)",
    };

    private static string FormatTags(IEnumerable<KeyValuePair<string, object?>>? tags) =>
        tags is null ? string.Empty : string.Join(",", tags.Select(t => $"{t.Key}={t.Value}"));

    private static string FormatTags(ReadOnlyTagCollection tags)
    {
        var pairs = new List<string>(tags.Count);
        foreach (var tag in tags)
        {
            pairs.Add($"{tag.Key}={tag.Value}");
        }

        return string.Join(",", pairs);
    }

    protected override bool OnForceFlush(int timeoutMilliseconds)
    {
        if (_disposed)
        {
            return true;
        }

        try
        {
            _writer.Flush();
        }
        catch (Exception)
        {
            return false;
        }

        return true;
    }

    protected override bool OnShutdown(int timeoutMilliseconds)
    {
        if (_disposed)
        {
            return true;
        }

        try
        {
            _writer.Flush();
        }
        catch (Exception)
        {
            return false;
        }

        return true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _writer.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>
/// Returned when the <c>file</c> destination's own setup fails (bad path,
/// permissions, a read-only filesystem) — export failure, including at setup
/// time, must never affect startup (U10). Silently accepts and discards
/// everything; the failure was already reported once, at setup, to stderr.
/// </summary>
internal sealed class NoopExporter<T> : BaseExporter<T>
    where T : class
{
    public override ExportResult Export(in Batch<T> batch) => ExportResult.Success;
}
