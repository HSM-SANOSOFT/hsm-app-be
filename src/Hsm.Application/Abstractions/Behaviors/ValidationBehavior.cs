using FluentValidation;
using FluentValidation.Results;

namespace Hsm.Application.Abstractions.Behaviors;

/// <summary>
/// Runs every FluentValidation validator registered for the request type and
/// throws with the merged failures. This is validation's ONLY home: an HTTP
/// caller, a Blazor circuit and a queued job all reach the handler through this
/// stage, so a rule written once holds on every path. It sits behind
/// authorization on purpose — a caller who may not perform the action is
/// refused before the system spends work checking their payload.
/// </summary>
public sealed class ValidationBehavior<TRequest, TResult>(
    IEnumerable<IValidator<TRequest>> validators) : IPipelineBehavior<TRequest, TResult>
    where TRequest : IRequest<TResult>
{
    public async Task<TResult> HandleAsync(
        TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken ct)
    {
        var failures = new List<ValidationFailure>();
        foreach (var validator in validators)
        {
            // A fresh ValidationContext per validator, deliberately: FluentValidation
            // accumulates failures ONTO a shared context across successive Validate
            // calls, so reusing one context across validators of the same request
            // type would double-count — the second validator's result would already
            // contain the first's failures, and AddRange below would duplicate them.
            var context = new ValidationContext<TRequest>(request);
            var result = await validator.ValidateAsync(context, ct).ConfigureAwait(false);
            failures.AddRange(result.Errors);
        }

        // Every validator runs before anything throws, so a caller sees all of
        // their mistakes at once rather than one per round trip.
        return failures.Count > 0 ? throw new ValidationException(failures) : await next().ConfigureAwait(false);
    }
}
