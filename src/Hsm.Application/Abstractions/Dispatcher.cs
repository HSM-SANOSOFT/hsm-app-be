using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Hsm.Application.Abstractions;

/// <summary>
/// Resolves the handler for a request's concrete type and wraps it in the
/// registered behavior chain. The open generic (IRequest&lt;TResult&gt;) means the
/// concrete type is only known at runtime, so the invoker is built once per
/// request type by reflection and cached — the reflection cost is paid on first
/// dispatch, never per call.
/// </summary>
public sealed class Dispatcher(IServiceProvider provider) : IDispatcher
{
    private static readonly ConcurrentDictionary<Type, object> Invokers = new();

    public Task<TResult> Send<TResult>(IRequest<TResult> request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var invoker = (Func<IServiceProvider, IRequest<TResult>, CancellationToken, Task<TResult>>)
            Invokers.GetOrAdd(request.GetType(), static type => BuildInvoker<TResult>(type));

        return invoker(provider, request, ct);
    }

    private static object BuildInvoker<TResult>(Type requestType)
    {
        var method = typeof(Dispatcher)
            .GetMethod(nameof(InvokeTyped), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(requestType, typeof(TResult));

        return method.CreateDelegate(
            typeof(Func<IServiceProvider, IRequest<TResult>, CancellationToken, Task<TResult>>));
    }

    private static Task<TResult> InvokeTyped<TRequest, TResult>(
        IServiceProvider provider, IRequest<TResult> request, CancellationToken ct)
        where TRequest : IRequest<TResult>
    {
        var handler = provider.GetService<IRequestHandler<TRequest, TResult>>()
            ?? throw new InvalidOperationException(
                $"No handler registered for {typeof(TRequest).Name}.");

        var typed = (TRequest)request;
        RequestHandlerDelegate<TResult> next = () => handler.HandleAsync(typed, ct);

        // Behaviors apply outermost-first, so wrap in reverse registration order.
        var behaviors = provider.GetServices<IPipelineBehavior<TRequest, TResult>>().ToArray();
        for (var i = behaviors.Length - 1; i >= 0; i--)
        {
            var behavior = behaviors[i];
            var inner = next;
            next = () => behavior.HandleAsync(typed, inner, ct);
        }

        return next();
    }
}
