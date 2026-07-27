using System.Text.Json;
using System.Text.Json.Nodes;
using HandlebarsDotNet;
using Hsm.Application.Templates;

namespace Hsm.Infrastructure.Templates;

/// <summary>
/// Handlebars.Net adapter for <see cref="ITemplateRenderer"/> — the frozen
/// composeTemplate semantics: HTML escaping ON, no custom helpers, and
/// base-template inheritance by rendering the base with the child's output
/// injected as <c>body</c>.
/// </summary>
public sealed class HandlebarsTemplateRenderer : ITemplateRenderer
{
    private readonly IHandlebars _engine = Handlebars.Create();

    public void AssertCompiles(string content)
    {
        try
        {
            _engine.Compile(content);
        }
        catch (HandlebarsException exception)
        {
            throw new TemplateRenderException(exception.Message);
        }
    }

    public string Render(string content, string? baseContent, JsonObject data)
    {
        try
        {
            var context = ToContext(data);
            var childHtml = _engine.Compile(content)(context);
            if (baseContent is null)
            {
                return childHtml;
            }

            var baseContext = ToContext(data);
            baseContext["body"] = childHtml;
            return _engine.Compile(baseContent)(baseContext);
        }
        catch (HandlebarsException exception)
        {
            throw new TemplateRenderException(exception.Message);
        }
    }

    /// <summary>JSON → plain dictionaries/lists/primitives, the shape Handlebars.Net binds to.</summary>
    private static Dictionary<string, object?> ToContext(JsonObject data)
    {
        var context = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in data)
        {
            context[property.Key] = ToValue(property.Value);
        }

        return context;
    }

    private static object? ToValue(JsonNode? node) => node switch
    {
        null => null,
        JsonObject obj => ToContext(obj),
        JsonArray array => array.Select(ToValue).ToList(),
        _ => node.GetValueKind() switch
        {
            JsonValueKind.String => node.GetValue<string>(),
            JsonValueKind.Number => node.AsValue().TryGetValue<long>(out var integer)
                ? integer
                : node.GetValue<double>(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        },
    };
}
