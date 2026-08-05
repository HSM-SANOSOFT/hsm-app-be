using System.Reflection;
using FluentValidation;
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
        return services.AddHsmValidators();
    }

    /// <summary>
    /// Every AbstractValidator in Hsm.Application, registered as
    /// IValidator&lt;T&gt;. Scanning rather than listing is deliberate: a
    /// validator that exists but was never registered is a rule that silently
    /// does not run, which is the worst failure mode available here.
    /// </summary>
    public static IServiceCollection AddHsmValidators(this IServiceCollection services) =>
        services.AddValidatorsFromAssembly(
            Assembly.GetAssembly(typeof(PipelineRegistration))!,
            ServiceLifetime.Scoped,
            includeInternalTypes: false);
}
