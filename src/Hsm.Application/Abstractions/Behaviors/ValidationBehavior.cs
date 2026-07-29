using Hsm.Application.Errors;

namespace Hsm.Application.Abstractions.Behaviors;

public sealed class ValidationBehavior<TRequest, TResult>(
    IEnumerable<IValidator<TRequest>> validators) : IPipelineBehavior<TRequest, TResult>
    where TRequest : IRequest<TResult>
{
    public Task<TResult> HandleAsync(
        TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken ct)
    {
        var failures = validators.SelectMany(v => v.Validate(request)).ToList();
        return failures.Count > 0 ? throw ApiException.Validation(failures) : next();
    }
}
