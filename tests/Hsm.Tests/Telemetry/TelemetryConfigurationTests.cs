using System.Diagnostics;
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
/// <para><b>How exporter registration is asserted without a collector or
/// reflection into the OpenTelemetry SDK's private provider internals:</b>
/// <c>AddHsmTelemetry</c> registers the resolved <see cref="TelemetryOptions"/>
/// (which exporters were selected, per signal, plus the OTLP endpoint) as a
/// DI singleton. A test builds the host and reads that singleton back — the
/// exact decision the registration made, not an inference about the SDK's
/// object graph. The one exception is the file-exporter test at the bottom,
/// which resolves the real <see cref="TracerProvider"/> and forces a flush,
/// because the file destination's own correctness (does a line actually land
/// on disk) is not something the options record can attest to by itself.</para>
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
        var builder = BuilderWith(("Telemetry:Exporters", "console"));

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

        tracerProvider.ForceFlush();

        var filePath = Path.Combine(directory, "file-exporter-test-traces.log");
        Assert.True(File.Exists(filePath), $"expected a traces file at {filePath}");
        var content = await File.ReadAllTextAsync(filePath);
        Assert.Contains("telemetry-configuration-test-span", content, StringComparison.Ordinal);

        Directory.Delete(directory, recursive: true);
    }
}
