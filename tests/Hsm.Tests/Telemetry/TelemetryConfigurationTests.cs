using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Hsm.Infrastructure.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Trace;

namespace Hsm.Tests.Telemetry;

/// <summary>
/// Task 23: <c>AddHsmTelemetry</c> reads <c>Telemetry:Exporters</c> (plus
/// per-signal overrides) instead of the three hosts each hard-wiring an OTLP
/// destination. These tests never touch a network — that is the point of the
/// mechanism below.
///
/// <para><b>How exporter SELECTION is asserted without a collector or
/// reflection into the OpenTelemetry SDK's private provider internals:</b>
/// <c>AddHsmTelemetry</c> registers the resolved <see cref="TelemetryOptions"/>
/// (which exporters were selected, per signal, plus the OTLP endpoint) as a
/// DI singleton. A test builds the host and reads that singleton back — the
/// exact decision the registration made, not an inference about the SDK's
/// object graph.</para>
///
/// <para><b>Two things the options record cannot attest to by itself</b> get
/// their own tests that resolve the real <see cref="TracerProvider"/>: does a
/// line actually land on disk for the <c>file</c> destination (it resolves a
/// <see cref="StreamWriter"/>, a path, permissions — real I/O), and does an
/// OTLP export actually go to the right WIRE PATH. That second one is not
/// hypothetical: an earlier version of this file assigned
/// <c>OtlpExporterOptions.Endpoint</c> to the bare configured base, which
/// (contrary to what <c>UseOtlpExporter(protocol, baseUri)</c> did before this
/// task) makes the SDK stop auto-appending <c>/v1/{signal}</c> — every signal
/// silently POSTed to the bare base path, a 404 the exporter swallows exactly
/// like any other export failure. Nothing about inspecting `TelemetryOptions`,
/// or even calling `ForceFlush()` and getting `true` back, would have caught
/// this: both only see that export was ATTEMPTED, never where it actually
/// went. Only a real listener on the wire, reading the actual path a byte left
/// the process on, catches it — see
/// <see cref="Otlp_traces_are_posted_to_the_v1_traces_path_not_the_bare_configured_endpoint"/>.</para>
/// </summary>
public class TelemetryConfigurationTests
{
    private static HostApplicationBuilder BuilderWith(params (string Key, string Value)[] settings)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(
            settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value))!);
        return builder;
    }

    [Fact]
    public void Console_only_configuration_registers_no_otlp_exporter_for_any_signal()
    {
        // A malformed endpoint hardens this test: if console-only ever
        // accidentally touched the OTLP path (a regression that would try to
        // construct an exporter from it), `new Uri(...)` on this value would
        // throw immediately. It doesn't — proving otlp is never even looked
        // at when it isn't selected, not merely that it isn't registered.
        var builder = BuilderWith(
            ("Telemetry:Exporters", "console"),
            ("Telemetry:Otlp:Endpoint", "not a valid uri at all"));

        builder.AddHsmTelemetry("test-service");

        using var host = builder.Build();
        var options = host.Services.GetRequiredService<TelemetryOptions>();

        Assert.True(options.Traces.Console);
        Assert.False(options.Traces.Otlp);
        Assert.False(options.Metrics.Otlp);
        Assert.False(options.Logs.Otlp);
    }

    [Fact]
    public void Otlp_without_a_configured_endpoint_fails_fast_instead_of_a_silent_no_op()
    {
        var builder = BuilderWith(("Telemetry:Exporters", "otlp"));

        var exception = Assert.Throws<InvalidOperationException>(() => builder.AddHsmTelemetry("test-service"));

        Assert.Contains("Telemetry:Otlp:Endpoint", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Otlp_with_a_configured_endpoint_builds_the_host_without_touching_the_network()
    {
        var builder = BuilderWith(
            ("Telemetry:Exporters", "otlp"),
            ("Telemetry:Otlp:Endpoint", "http://127.0.0.1:1"));

        builder.AddHsmTelemetry("test-service");

        // Building never dials the endpoint — OTLP export is async/buffered.
        // A refused connection later is the harmless case this task protects
        // (U10): it must never surface here.
        using var host = builder.Build();
        var options = host.Services.GetRequiredService<TelemetryOptions>();

        Assert.True(options.Traces.Otlp);
    }

    [Fact]
    public void A_per_signal_override_replaces_the_default_exporter_list_only_for_that_signal()
    {
        var builder = BuilderWith(
            ("Telemetry:Exporters", "console"),
            ("Telemetry:Metrics:Exporters", "otlp"),
            ("Telemetry:Otlp:Endpoint", "http://127.0.0.1:1"));

        builder.AddHsmTelemetry("test-service");

        using var host = builder.Build();
        var options = host.Services.GetRequiredService<TelemetryOptions>();

        Assert.False(options.Traces.Otlp);
        Assert.True(options.Metrics.Otlp);
        Assert.False(options.Logs.Otlp);
        Assert.True(options.Traces.Console);
    }

    [Fact]
    public void An_unknown_exporter_token_fails_fast_with_the_bad_token_named()
    {
        var builder = BuilderWith(("Telemetry:Exporters", "console,carrier-pigeon"));

        var exception = Assert.Throws<InvalidOperationException>(() => builder.AddHsmTelemetry("test-service"));

        Assert.Contains("carrier-pigeon", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void No_telemetry_configuration_at_all_defaults_to_console_only()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.AddHsmTelemetry("test-service");

        using var host = builder.Build();
        var options = host.Services.GetRequiredService<TelemetryOptions>();

        Assert.True(options.Traces.Console);
        Assert.False(options.Traces.Otlp);
        Assert.False(options.Traces.File);
    }

    [Fact]
    public async Task File_exporter_writes_a_line_for_a_span_on_the_Hsm_Application_source()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"hsm-telemetry-test-{Guid.NewGuid():N}");
        var builder = BuilderWith(
            ("Telemetry:Exporters", "file"),
            ("Telemetry:File:Directory", directory));

        builder.AddHsmTelemetry("file-exporter-test");

        using var host = builder.Build();
        var tracerProvider = host.Services.GetRequiredService<TracerProvider>();

        // Matches by ActivitySource NAME (RequestActivity.SourceName,
        // "Hsm.Application") — AddSource listens by name, not by instance, so
        // this stand-in source is heard exactly as the pipeline's own would be.
        using var source = new ActivitySource("Hsm.Application");
        using (var activity = source.StartActivity("telemetry-configuration-test-span"))
        {
            activity?.SetTag("test", "file-exporter");
        }

        tracerProvider.ForceFlush(10_000);

        var filePath = Path.Combine(directory, "file-exporter-test-traces.log");
        Assert.True(File.Exists(filePath), $"expected a traces file at {filePath}");
        var content = await File.ReadAllTextAsync(filePath);
        Assert.Contains("telemetry-configuration-test-span", content, StringComparison.Ordinal);

        Directory.Delete(directory, recursive: true);
    }

    /// <summary>
    /// Export failure must never affect startup (U10) — including AT SETUP,
    /// for a destination that has to touch the filesystem. A regular FILE
    /// sitting where the file destination wants to create a DIRECTORY makes
    /// <c>Directory.CreateDirectory</c> throw; that must degrade this
    /// signal's file export to a no-op for the run, not take the host down.
    /// </summary>
    [Fact]
    public void Uncreatable_file_destination_path_degrades_to_a_no_op_instead_of_crashing_startup()
    {
        var blockingFile = Path.Combine(Path.GetTempPath(), $"hsm-telemetry-blocker-{Guid.NewGuid():N}");
        File.WriteAllText(blockingFile, "occupies the path a directory needs");
        try
        {
            var builder = BuilderWith(
                ("Telemetry:Exporters", "file"),
                ("Telemetry:File:Directory", blockingFile));

            builder.AddHsmTelemetry("degraded-file-test");

            using var host = builder.Build();
            var tracerProvider = host.Services.GetRequiredService<TracerProvider>();

            using var source = new ActivitySource("Hsm.Application");
            using (source.StartActivity("degraded-file-test-span"))
            {
            }

            // Neither AddHsmTelemetry, nor Build(), nor ForceFlush() throws —
            // the file destination silently became a no-op for this run.
            var flushed = tracerProvider.ForceFlush(5_000);
            Assert.True(flushed);
        }
        finally
        {
            File.Delete(blockingFile);
        }
    }

    /// <summary>
    /// The wire-level check: <c>OtlpExporterOptions.Endpoint</c>, once
    /// assigned programmatically, is used EXACTLY as given — the SDK's own
    /// "/v1/{signal}" auto-append only fires when the option is left at its
    /// default. A naive <c>otlp.Endpoint = new Uri(configuredBase)</c> (what
    /// this file had before this was caught) posts every signal to the BARE
    /// configured base instead of its signal path — a 404 the OTLP exporter
    /// treats as an ordinary export failure, so it never surfaces anywhere
    /// else. A tiny real HTTP listener is the only way to see the actual path
    /// a byte left the process on.
    /// </summary>
    [Fact]
    public async Task Otlp_traces_are_posted_to_the_v1_traces_path_not_the_bare_configured_endpoint()
    {
        using var listener = new HttpListener();
        var port = GetFreeTcpPort();
        var prefix = $"http://127.0.0.1:{port}/";
        listener.Prefixes.Add(prefix);
        listener.Start();

        var getContext = listener.GetContextAsync();

        var builder = BuilderWith(
            ("Telemetry:Exporters", "otlp"),
            ("Telemetry:Otlp:Endpoint", prefix));

        builder.AddHsmTelemetry("wire-path-test");

        using var host = builder.Build();
        var tracerProvider = host.Services.GetRequiredService<TracerProvider>();

        using var source = new ActivitySource("Hsm.Application");
        using (source.StartActivity("wire-path-test-span"))
        {
        }

        tracerProvider.ForceFlush(10_000);

        var contextTask = await Task.WhenAny(getContext, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(getContext, contextTask);

        var context = await getContext;
        var path = context.Request.Url?.AbsolutePath;
        context.Response.StatusCode = 200;
        context.Response.Close();

        Assert.Equal("/v1/traces", path);
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
