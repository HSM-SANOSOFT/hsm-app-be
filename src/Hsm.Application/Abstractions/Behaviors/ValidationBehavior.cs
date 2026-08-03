namespace Hsm.Application.Abstractions.Behaviors;

/// <summary>
/// Renamed IValidator/ValidationFailure to FluentValidation's own
/// <see cref="FluentValidation.ValidationException"/> — the closed set's one
/// 400 vehicle (see <c>HsmException</c>'s doc comment on why there is no
/// general BadRequestException). This hand-rolled <see cref="IValidator{TRequest}"/>
/// surface is replaced outright by real FluentValidation validators in Task 3;
/// only the exception thrown here changes in this task.
/// </summary>
public sealed class ValidationBehavior<TRequest, TResult>(
    IEnumerable<IValidator<TRequest>> validators) : IPipelineBehavior<TRequest, TResult>
    where TRequest : IRequest<TResult>
{
    public Task<TResult> HandleAsync(
        TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken ct)
    {
        var failures = validators.SelectMany(v => v.Validate(request)).ToList();
        return failures.Count > 0
            ? throw new FluentValidation.ValidationException(
                [.. failures.Select(f => new FluentValidation.Results.ValidationFailure(f.Field, f.Message))])
            : next();
    }
}
