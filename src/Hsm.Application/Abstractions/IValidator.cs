namespace Hsm.Application.Abstractions;

/// <summary>
/// Business-rule validation for one request type. HTTP-shape validation stays at
/// the edge (Hsm.Api's BodyValidator) — this is for rules the application owns.
/// </summary>
public interface IValidator<in TRequest>
{
    IEnumerable<ValidationFailure> Validate(TRequest request);
}

/// <summary>
/// One failed constraint. Field and Key are wire contract: the frozen error
/// envelope carries per-field constraint keys the frontend maps to copy.
/// </summary>
public sealed record ValidationFailure(string Field, string Key, string Message);
