using System.Diagnostics;

namespace Hsm.Application.Abstractions.Behaviors;

public static class RequestActivity
{
    public const string SourceName = "Hsm.Application";
    internal static readonly ActivitySource Source = new(SourceName);
}

/// <summary>
/// Outermost behavior: every dispatched request gets a span named after its
/// type, including requests the pipeline goes on to reject. OpenTelemetry is
/// already wired in every host (plan 2026-07-27-001 U10); this only needs the
/// meter/source name added to the hosts' AddMeter/AddSource lists.
/// </summary>
public sealed class TelemetryBehavior<TRequest, TResult> : IPipelineBehavior<TRequest, TResult>
    where TRequest : IRequest<TResult>
{
    public async Task<TResult> HandleAsync(
        TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken ct)
    {
        using var activity = RequestActivity.Source.StartActivity(typeof(TRequest).Name);
        try
        {
            var result = await next().ConfigureAwait(false);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return result;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }
}
