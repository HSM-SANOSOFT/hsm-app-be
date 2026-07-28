using System.Diagnostics.Metrics;
using Hsm.Web.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;

namespace Hsm.Contract.Tests;

/// <summary>
/// Circuit-signal behavior (plan U10): the active-circuit gauge rises on
/// open and falls on close; disconnects count separately. Lives here because
/// this project references the web host.
/// </summary>
public sealed class CircuitMetricsTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly CircuitMetrics _metrics;
    private readonly MetricCollector<long> _active;
    private readonly MetricCollector<long> _disconnects;

    public CircuitMetricsTests()
    {
        var services = new ServiceCollection();
        services.AddMetrics();
        _provider = services.BuildServiceProvider();

        var meterFactory = _provider.GetRequiredService<IMeterFactory>();
        _metrics = new CircuitMetrics(meterFactory);
        _active = new MetricCollector<long>(meterFactory, CircuitMetrics.MeterName, "hsm.web.circuits.active");
        _disconnects = new MetricCollector<long>(meterFactory, CircuitMetrics.MeterName, "hsm.web.circuits.disconnects");
    }

    public void Dispose()
    {
        _active.Dispose();
        _disconnects.Dispose();
        _provider.Dispose();
    }

    [Fact]
    public void Active_count_rises_on_open_and_falls_on_close()
    {
        _metrics.CircuitOpened();
        _metrics.CircuitOpened();
        _metrics.CircuitClosed();

        var total = _active.GetMeasurementSnapshot().Sum(m => m.Value);
        Assert.Equal(1, total);
    }

    [Fact]
    public void Disconnects_count_without_touching_active()
    {
        _metrics.CircuitOpened();
        _metrics.ConnectionDown();
        _metrics.ConnectionDown();

        Assert.Equal(2, _disconnects.GetMeasurementSnapshot().Sum(m => m.Value));
        Assert.Equal(1, _active.GetMeasurementSnapshot().Sum(m => m.Value));
    }
}
