using FluentValidation;
using FluentValidation.Results;
using Hsm.Application.Errors;

namespace Hsm.Application.Templates;

/// <summary>Shared error factories mirroring the frozen templates.error.ts family.</summary>
public static class TemplateErrors
{
    public static NotFoundException NotFound(string identifier) => new("Template", identifier);

    public static ConflictException AlreadyExists(string name) =>
        new($"Template with name '{name}' already exists");

    public static ConflictException InUse(Guid id) =>
        new($"Template '{id}' is referenced as a base by other templates and cannot be deleted");

    // InvalidShape/InvalidHandlebars are not in Task 2's sweep table (Templates
    // is Task 8's module); retargeted onto the same ValidationException vehicle
    // as every other shape refusal in the closed set, so the file compiles
    // without inventing a new exception type. Task 8 revisits these call sites
    // with real validators.
    public static ValidationException InvalidShape(string reason) =>
        new([new ValidationFailure("template", $"Invalid template payload: {reason}")]);

    public static ValidationException InvalidHandlebars(string message) =>
        new([new ValidationFailure("template", $"Invalid Handlebars template: {message}")]);
}
