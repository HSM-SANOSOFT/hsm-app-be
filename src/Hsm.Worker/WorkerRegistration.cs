using Hsm.Application.Abstractions;
using Hsm.Infrastructure.Jobs;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hsm.Worker;

/// <summary>
/// Everything a host needs in order to PROCESS jobs, as opposed to enqueue
/// them. In production exactly one host calls this: <c>Hsm.Worker</c>.
/// </summary>
public static class WorkerRegistration
{
    /// <summary>
    /// The consume loops and the delayed-job pump, plus the
    /// <see cref="AmbientPrincipal"/> the consumer puts each job's enqueuing
    /// actor on. The principal is registered here because consumption cannot
    /// work without it; which <c>ICurrentPrincipal</c> a host reads is still the
    /// host's own decision (the worker reads the ambient one — there is no
    /// request to read).
    /// </summary>
    public static IServiceCollection AddHsmJobProcessing(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<AmbientPrincipal>();
        services.AddHostedService<JobConsumerService>();
        services.AddHostedService<DelayedJobPump>();
        return services;
    }
}
