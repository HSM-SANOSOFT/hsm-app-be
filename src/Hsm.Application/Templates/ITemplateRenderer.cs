using System.Text.Json.Nodes;

namespace Hsm.Application.Templates;

/// <summary>
/// Raised by <see cref="ITemplateRenderer"/> when Handlebars compilation or
/// rendering fails; the message is the engine's own diagnostic (surfaced in
/// the frozen "Invalid Handlebars template: ..." error).
/// </summary>
public sealed class TemplateRenderException(string message) : Exception(message);

/// <summary>
/// Handlebars rendering port (frozen composeTemplate): HTML escaping on, no
/// custom helpers, and base-template inheritance by injecting the rendered
/// child as <c>body</c> into the base.
/// </summary>
public interface ITemplateRenderer
{
    /// <summary>Throws <see cref="TemplateRenderException"/> when the source does not compile.</summary>
    void AssertCompiles(string content);

    /// <summary>Renders <paramref name="content"/> (wrapped by <paramref name="baseContent"/> when given).</summary>
    string Render(string content, string? baseContent, JsonObject data);
}
