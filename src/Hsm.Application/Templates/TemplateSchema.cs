using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Hsm.Application.Templates;

/// <summary>One schema-validation finding.</summary>
public sealed record TemplateSchemaIssue(string Path, string Expected, string Received);

/// <summary>
/// The mini-schema language: leaves are the
/// type tags string/number/boolean/date/any (suffix "?" marks optional),
/// objects are sub-schemas, and single-element arrays are lists of the element
/// sub-schema. Extra data keys are ignored; missing non-optional values fail.
/// </summary>
public static class TemplateSchema
{
    private static readonly string[] PrimitiveTags = ["string", "number", "boolean", "date", "any"];

    public static bool IsWellFormed(JsonNode? schema)
    {
        if (schema is JsonValue value && value.GetValueKind() == JsonValueKind.String)
        {
            return PrimitiveTags.Contains(StripOptional(value.GetValue<string>()), StringComparer.Ordinal);
        }

        if (schema is JsonArray array)
        {
            return array.Count == 1 && IsWellFormed(array[0]);
        }

        if (schema is JsonObject obj)
        {
            return obj.All(property => IsWellFormed(property.Value));
        }

        return false;
    }

    public static IReadOnlyList<TemplateSchemaIssue> Validate(JsonNode? schema, JsonNode? data)
    {
        var issues = new List<TemplateSchemaIssue>();
        Walk(schema, data, present: data is not null, path: string.Empty, issues);
        return issues;
    }

    private static void Walk(JsonNode? schema, JsonNode? data, bool present, string path, List<TemplateSchemaIssue> issues)
    {
        var shownPath = path.Length == 0 ? "<root>" : path;

        if (schema is JsonValue tagValue && tagValue.GetValueKind() == JsonValueKind.String)
        {
            var raw = tagValue.GetValue<string>();
            var optional = raw.EndsWith('?');
            var tag = StripOptional(raw);
            if (!PrimitiveTags.Contains(tag, StringComparer.Ordinal))
            {
                // An unknown tag degrades to "any".
                tag = "any";
            }

            if (!present || data is null || data.GetValueKind() == JsonValueKind.Null)
            {
                if (!optional)
                {
                    issues.Add(new TemplateSchemaIssue(shownPath, tag, !present ? "undefined" : "null"));
                }

                return;
            }

            if (!MatchesPrimitive(tag, data))
            {
                issues.Add(new TemplateSchemaIssue(shownPath, tag, Describe(data)));
            }

            return;
        }

        if (schema is JsonArray schemaArray)
        {
            if (schemaArray.Count != 1)
            {
                issues.Add(new TemplateSchemaIssue(
                    shownPath, "malformed schema (array must have a single element)", "malformed"));
                return;
            }

            if (data is not JsonArray dataArray)
            {
                issues.Add(new TemplateSchemaIssue(shownPath, "array", !present ? "undefined" : Describe(data)));
                return;
            }

            for (var index = 0; index < dataArray.Count; index++)
            {
                Walk(schemaArray[0], dataArray[index], present: true, JoinPath(path, $"[{index}]"), issues);
            }

            return;
        }

        if (schema is JsonObject schemaObject)
        {
            if (data is not JsonObject dataObject)
            {
                issues.Add(new TemplateSchemaIssue(shownPath, "object", !present ? "undefined" : Describe(data)));
                return;
            }

            foreach (var property in schemaObject)
            {
                var childPresent = dataObject.ContainsKey(property.Key);
                Walk(property.Value, childPresent ? dataObject[property.Key] : null, childPresent,
                    JoinPath(path, property.Key), issues);
            }

            return;
        }

        issues.Add(new TemplateSchemaIssue(shownPath, "malformed schema", "malformed"));
    }

    private static bool MatchesPrimitive(string tag, JsonNode data)
    {
        var kind = data.GetValueKind();
        return tag switch
        {
            "string" => kind == JsonValueKind.String,
            "number" => kind == JsonValueKind.Number,
            "boolean" => kind is JsonValueKind.True or JsonValueKind.False,
            "date" => kind == JsonValueKind.Number
                || (kind == JsonValueKind.String
                    && DateTimeOffset.TryParse(
                        data.GetValue<string>(),
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AllowWhiteSpaces,
                        out _)),
            _ => true,
        };
    }

    private static string Describe(JsonNode? value) => value switch
    {
        null => "null",
        JsonArray => "array",
        JsonObject => "object",
        _ => value.GetValueKind() switch
        {
            JsonValueKind.Null => "null",
            JsonValueKind.String => "string",
            JsonValueKind.Number => "number",
            JsonValueKind.True or JsonValueKind.False => "boolean",
            _ => "object",
        },
    };

    private static string StripOptional(string tag) => tag.EndsWith('?') ? tag[..^1] : tag;

    private static string JoinPath(string parent, string child)
    {
        if (parent.Length == 0)
        {
            return child;
        }

        return child.StartsWith('[') ? parent + child : $"{parent}.{child}";
    }
}
