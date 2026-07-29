using System.Collections.Concurrent;
using System.Reflection;
using Hsm.Application.Errors;

namespace Hsm.Application.Abstractions.Behaviors;

/// <summary>
/// Authorization is a property of the request, not of the transport. Before this
/// existed, every edge enforced it separately — HTTP endpoints via
/// RequestAuth.GateAsync, in-process UI services via UiServiceGate — and a
/// forgotten call failed open. Here it cannot be forgotten: the same command
/// dispatched from HTTP, a Blazor circuit, or a queued job is gated identically.
/// Failures reuse ApiException so status codes and error envelopes stay
/// byte-identical to the frozen contract.
/// </summary>
public sealed class AuthorizationBehavior<TRequest, TResult>(ICurrentPrincipal principal)
    : IPipelineBehavior<TRequest, TResult>
    where TRequest : IRequest<TResult>
{
    private static readonly ConcurrentDictionary<Type, Policy> Policies = new();

    public Task<TResult> HandleAsync(
        TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken ct)
    {
        var policy = Policies.GetOrAdd(typeof(TRequest), static type => Policy.For(type));

        if (policy.AllowAnonymous)
        {
            return next();
        }

        var actor = principal.Actor ?? throw ApiException.Unauthorized();

        if (policy.Roles.Count > 0 && !policy.Roles.Any(actor.IsInRole))
        {
            throw ApiException.Forbidden();
        }

        if (!actor.OnboardingCompleted && !policy.AllowPendingOnboarding)
        {
            throw ApiException.Forbidden();
        }

        return next();
    }

    private sealed record Policy(bool AllowAnonymous, IReadOnlyList<string> Roles, bool AllowPendingOnboarding)
    {
        public static Policy For(Type type) => new(
            AllowAnonymous: type.GetCustomAttribute<AllowAnonymousRequestAttribute>() is not null,
            Roles: type.GetCustomAttribute<RequireRoleAttribute>()?.Roles ?? [],
            AllowPendingOnboarding: type.GetCustomAttribute<AllowPendingOnboardingAttribute>() is not null);
    }
}
