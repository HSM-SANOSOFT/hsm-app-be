using System.Collections.Concurrent;
using System.Reflection;
using Hsm.Application.Errors;

namespace Hsm.Application.Abstractions.Behaviors;

/// <summary>
/// Authorization is a property of the request, not of the transport. Before this
/// existed, every edge enforced it separately — HTTP endpoints via
/// RequestAuth.GateAsync, in-process UI services via UiServiceGate — and a
/// forgotten call failed open. Both of those checks are gone (plan Task 15);
/// this is now the ONLY authorizer, so it cannot be forgotten: the same command
/// dispatched from HTTP, a Blazor circuit, or a queued job is gated identically.
/// Failures throw the closed exception set (<see cref="UnauthorizedException"/>,
/// <see cref="ForbiddenException"/>), mapped to status codes by one handler per
/// transport.
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

        var actor = principal.Actor ?? throw new UnauthorizedException();

        if (policy.Roles.Count > 0 && !policy.Roles.Any(actor.IsInRole))
        {
            throw new ForbiddenException();
        }

        if (!actor.OnboardingCompleted && !policy.AllowPendingOnboarding)
        {
            throw new ForbiddenException("Onboarding is not complete.");
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
