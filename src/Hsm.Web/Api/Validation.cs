using System.Text.Json;
using System.Text.Json.Nodes;
using Hsm.Application.Errors;

namespace Hsm.Web.Api;

/// <summary>
/// A 400 whose envelope carries the frozen ValidationPipe shape:
/// issue.code COMMON.VALIDATION, issue.message as a string array, and
/// issue.errors as per-field machine-readable constraint keys.
/// </summary>
public sealed class ApiValidationException(
    IReadOnlyList<string> messages,
    IReadOnlyList<(string Field, IReadOnlyList<string> Constraints)> fieldConstraints)
    : ApiException(400, "Validation failed", ApiErrorCode.Validation)
{
    public IReadOnlyList<string> Messages { get; } = messages;

    public IReadOnlyList<(string Field, IReadOnlyList<string> Constraints)> FieldConstraints { get; } = fieldConstraints;
}

/// <summary>
/// Request-body validation reproducing the observable surface of the frozen
/// global ValidationPipe: class-validator constraint keys and messages,
/// whitelist enforcement (unknown properties are rejected), and all failures
/// reported together.
/// </summary>
public sealed class BodyValidator
{
    private static readonly string[] MalformedJsonConstraint = ["malformedJson"];

    private readonly JsonObject _body;
    private readonly HashSet<string> _knownFields = new(StringComparer.Ordinal);
    private readonly List<(string Field, string Key, string Message)> _failures = [];

    private BodyValidator(JsonObject body) => _body = body;

    /// <summary>Reads the JSON object body; a missing body validates as empty.</summary>
    public static async Task<BodyValidator> ReadAsync(HttpContext ctx)
    {
        using var reader = new StreamReader(ctx.Request.Body);
        var text = await reader.ReadToEndAsync();
        if (string.IsNullOrWhiteSpace(text))
        {
            return new BodyValidator([]);
        }

        try
        {
            return new BodyValidator(JsonNode.Parse(text) as JsonObject ?? []);
        }
        catch (JsonException)
        {
            throw new ApiValidationException(["Malformed JSON body"], [("body", MalformedJsonConstraint)]);
        }
    }

    public string? OptionalString(string field)
    {
        _knownFields.Add(field);
        var value = _body[field];
        if (value is null)
        {
            return null;
        }

        if (value.GetValueKind() != JsonValueKind.String)
        {
            Fail(field, "isString", $"{field} must be a string");
            return null;
        }

        return value.GetValue<string>();
    }

    public string RequiredString(string field, int? minLength = null, bool email = false, IReadOnlyList<string>? oneOf = null)
    {
        _knownFields.Add(field);
        var value = _body[field];
        if (value is null || value.GetValueKind() == JsonValueKind.Null)
        {
            // class-validator reports every failed constraint for a missing value.
            if (minLength is not null)
            {
                Fail(field, "minLength", $"{field} must be longer than or equal to {minLength} characters");
            }

            if (email)
            {
                Fail(field, "isEmail", $"{field} must be an email");
            }
            else
            {
                Fail(field, "isString", $"{field} must be a string");
                Fail(field, "isNotEmpty", $"{field} should not be empty");
            }

            if (oneOf is not null)
            {
                Fail(field, "isEnum", $"{field} must be one of the following values: {string.Join(", ", oneOf)}");
            }

            return string.Empty;
        }

        if (value.GetValueKind() != JsonValueKind.String)
        {
            Fail(field, email ? "isEmail" : "isString", email ? $"{field} must be an email" : $"{field} must be a string");
            return string.Empty;
        }

        var text = value.GetValue<string>();
        if (!email && text.Length == 0)
        {
            Fail(field, "isNotEmpty", $"{field} should not be empty");
        }

        if (minLength is not null && text.Length < minLength)
        {
            Fail(field, "minLength", $"{field} must be longer than or equal to {minLength} characters");
        }

        if (email && !LooksLikeEmail(text))
        {
            Fail(field, "isEmail", $"{field} must be an email");
        }

        if (oneOf is not null && !oneOf.Contains(text, StringComparer.Ordinal))
        {
            Fail(field, "isEnum", $"{field} must be one of the following values: {string.Join(", ", oneOf)}");
        }

        return text;
    }

    public double RequiredNumber(string field)
    {
        _knownFields.Add(field);
        var value = _body[field];
        if (value is null
            || value.GetValueKind() != JsonValueKind.Number)
        {
            Fail(field, "isNumber", $"{field} must be a number conforming to the specified constraints");
            return 0;
        }

        return value.GetValue<double>();
    }

    /// <summary>An optional array field whose content is ignored (frozen public-signup roles).</summary>
    public void IgnoredArray(string field)
    {
        _knownFields.Add(field);
        var value = _body[field];
        if (value is not null && value.GetValueKind() != JsonValueKind.Array)
        {
            Fail(field, "isArray", $"{field} must be an array");
        }
    }

    /// <summary>The frozen forbidNonWhitelisted: unknown properties are rejected.</summary>
    public void RejectUnknownFields()
    {
        foreach (var property in _body)
        {
            if (!_knownFields.Contains(property.Key))
            {
                Fail(property.Key, "whitelistValidation", $"property {property.Key} should not exist");
            }
        }
    }

    public void ThrowIfInvalid()
    {
        if (_failures.Count == 0)
        {
            return;
        }

        var messages = _failures.Select(f => f.Message).ToList();
        var byField = _failures
            .GroupBy(f => f.Field)
            .Select(g => (g.Key, (IReadOnlyList<string>)g.Select(f => f.Key).Distinct().ToList()))
            .ToList();
        throw new ApiValidationException(messages, byField);
    }

    private void Fail(string field, string key, string message) => _failures.Add((field, key, message));

    private static bool LooksLikeEmail(string value)
    {
        var at = value.IndexOf('@', StringComparison.Ordinal);
        if (at <= 0 || at == value.Length - 1 || value.Contains(' ', StringComparison.Ordinal))
        {
            return false;
        }

        var domain = value[(at + 1)..];
        return domain.Contains('.', StringComparison.Ordinal) && !domain.StartsWith('.') && !domain.EndsWith('.');
    }
}
