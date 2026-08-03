using Hsm.Application.Errors;

namespace Hsm.Application.Templates;

/// <summary>Shared error factories mirroring the frozen templates.error.ts family.</summary>
public static class TemplateErrors
{
    public static ApiException NotFound(string identifier) =>
        ApiException.NotFound($"Template '{identifier}' not found");

    public static ApiException AlreadyExists(string name) =>
        new(409, $"Template with name '{name}' already exists", errorLabel: "Conflict");

    public static ApiException InUse(Guid id) =>
        new(
            409,
            $"Template '{id}' is referenced as a base by other templates and cannot be deleted",
            errorLabel: "Conflict");

    public static ApiException InvalidShape(string reason) =>
        ApiException.BadRequest($"Invalid template payload: {reason}");

    public static ApiException InvalidHandlebars(string message) =>
        ApiException.BadRequest($"Invalid Handlebars template: {message}");
}
