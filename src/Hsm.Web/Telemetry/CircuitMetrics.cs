using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace Hsm.Web.Telemetry;

/// <summary>
/// Blazor circuit signals (plan U10): active circuit count, opens, and
/// disconnect rate. A circuit leak presents identically to a memory leak
/// without these — correlate <c>hsm.web.circuits.active</c> with the runtime
/// instrumentation's process memory to tell them apart.
/// </summary>
public sealed class CircuitMetrics
{
    public const string MeterName = "Hsm.Web.Circuits";

    private readonly UpDownCounter<long> _active;
    private readonly Counter<long> _opened;
    private readonly Counter<long> _disconnects;

    public CircuitMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        var meter = meterFactory.Create(MeterName);
        _active = meter.CreateUpDownCounter<long>("hsm.web.circuits.active", "{circuit}", "Circuits currently open");
        _opened = meter.CreateCounter<long>("hsm.web.circuits.opened", "{circuit}", "Circuits opened since start");
        _disconnects = meter.CreateCounter<long>("hsm.web.circuits.disconnects", "{disconnect}", "Client connections dropped while their circuit stayed alive");
    }

    public void CircuitOpened()
    {
        _opened.Add(1);
        _active.Add(1);
    }

    public void CircuitClosed() => _active.Add(-1);

    public void ConnectionDown() => _disconnects.Add(1);
}

public sealed class MetricsCircuitHandler(CircuitMetrics metrics) : CircuitHandler
{
    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        metrics.CircuitOpened();
        return Task.CompletedTask;
    }

    public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        metrics.CircuitClosed();
        return Task.CompletedTask;
    }

    public override Task OnConnectionDownAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        metrics.ConnectionDown();
        return Task.CompletedTask;
    }
}
