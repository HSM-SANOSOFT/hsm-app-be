using Hsm.Application.Abstractions.Behaviors;
using Microsoft.Extensions.DependencyInjection;

namespace Hsm.Application.Abstractions;

public static class PipelineRegistration
{
    /// <summary>
    /// Registers the dispatcher and the behavior chain, outermost first.
    /// Order is load-bearing: telemetry records rejections, authorization
    /// refuses before validation spends work, and the transaction is innermost
    /// so it never wraps a request that was going to be refused.
    /// </summary>
    public static IServiceCollection AddHsmPipeline(this IServiceCollection services)
    {
        services.AddScoped<IDispatcher, Dispatcher>();
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TelemetryBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(AuthorizationBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));
        return services;
    }
}
