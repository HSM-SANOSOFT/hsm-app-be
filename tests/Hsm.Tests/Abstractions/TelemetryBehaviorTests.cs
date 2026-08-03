using System.Diagnostics;
using Hsm.Application.Abstractions;
using Hsm.Application.Abstractions.Behaviors;

namespace Hsm.Tests.Abstractions;

public class TelemetryBehaviorTests : IDisposable
{
    private sealed record Ping : IQuery<string>;

    private readonly ActivityListener _listener;
    private readonly List<Activity> _started = [];

    public TelemetryBehaviorTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == RequestActivity.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = activity => _started.Add(activity),
        };
        ActivitySource.AddActivityListener(_listener);
    }

    // CA1816: the brief's exact Dispose body is a one-line delegation to the
    // listener; GC.SuppressFinalize is moot since this type has no finalizer.
#pragma warning disable CA1816
    public void Dispose() => _listener.Dispose();
#pragma warning restore CA1816

    [Fact]
    public async Task Span_is_named_after_the_request_type()
    {
        var behavior = new TelemetryBehavior<Ping, string>();

        var result = await behavior.HandleAsync(
            new Ping(), () => Task.FromResult("ok"), CancellationToken.None);

        Assert.Equal("ok", result);
        Assert.Contains(_started, a => a.DisplayName == nameof(Ping));
    }

    [Fact]
    public async Task Failing_request_marks_the_span_as_error()
    {
        var behavior = new TelemetryBehavior<Ping, string>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => behavior.HandleAsync(
            new Ping(),
            () => throw new InvalidOperationException("boom"),
            CancellationToken.None));

        var span = Assert.Single(_started, a => a.DisplayName == nameof(Ping));
        Assert.Equal(ActivityStatusCode.Error, span.Status);
    }
}
