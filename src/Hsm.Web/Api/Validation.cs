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
/// The shared failure collector behind body and query validation: ordered
/// failures, grouped per field with distinct constraint keys when thrown as
/// the frozen ValidationPipe envelope.
/// </summary>
public sealed class ValidationFailures
{
    private readonly List<(string Field, string Key, string Message)> _failures = [];

    public int Count => _failures.Count;

    public void Add(string field, string key, string message) => _failures.Add((field, key, message));

    public void ThrowIfAny()
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
    private readonly ValidationFailures _failures = new();

    private BodyValidator(JsonObject body) => _body = body;

    /// <summary>Reads the JSON object body; a missing body validates as empty.</summary>
    public static async Task<BodyValidator> ReadAsync(HttpContext ctx)
    {
        // Buffered as UTF-8 bytes and parsed directly — no intermediate
        // string decode.
        using var buffer = new MemoryStream();
        await ctx.Request.Body.CopyToAsync(buffer, ctx.RequestAborted);
        if (!buffer.TryGetBuffer(out var segment))
        {
            segment = new ArraySegment<byte>(buffer.ToArray());
        }

        var bytes = SkipUtf8Preamble(segment.AsMemory());
        if (IsBlank(bytes.Span))
        {
            return new BodyValidator([]);
        }

        try
        {
            return new BodyValidator(JsonNode.Parse(bytes.Span) as JsonObject ?? []);
        }
        catch (JsonException)
        {
            throw new ApiValidationException(["Malformed JSON body"], [("body", MalformedJsonConstraint)]);
        }
    }

    private static ReadOnlyMemory<byte> SkipUtf8Preamble(ReadOnlyMemory<byte> bytes) =>
        bytes.Span.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) ? bytes[3..] : bytes;

    private static bool IsBlank(ReadOnlySpan<byte> bytes)
    {
        foreach (var b in bytes)
        {
            if (b is not ((byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// A validator over a nested object sharing this body's failure list,
    /// with failures reported under <paramref name="pathPrefix"/>.
    /// </summary>
    public NestedValidator Scope(JsonObject node, string pathPrefix) =>
        new(node, pathPrefix, _failures);

    /// <summary>The body's failure list, for traversals that report into it directly.</summary>
    internal ValidationFailures Failures => _failures;

    public string? OptionalString(string field, bool notEmpty = false)
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

        var text = value.GetValue<string>();
        if (notEmpty && text.Length == 0)
        {
            Fail(field, "isNotEmpty", $"{field} should not be empty");
        }

        return text;
    }

    public string RequiredString(
        string field,
        int? minLength = null,
        bool email = false,
        IReadOnlyList<string>? oneOf = null,
        string oneOfConstraint = "isEnum")
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
                Fail(field, oneOfConstraint, $"{field} must be one of the following values: {string.Join(", ", oneOf)}");
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
            Fail(field, oneOfConstraint, $"{field} must be one of the following values: {string.Join(", ", oneOf)}");
        }

        return text;
    }

    /// <summary>
    /// A field whose ONLY constraint is @IsEnum (frozen settings category):
    /// missing, non-string, and out-of-set values all surface the single
    /// isEnum constraint.
    /// </summary>
    public string RequiredEnum(string field, IReadOnlyList<string> oneOf)
    {
        _knownFields.Add(field);
        var value = _body[field];
        if (value is null
            || value.GetValueKind() != JsonValueKind.String
            || !oneOf.Contains(value.GetValue<string>(), StringComparer.Ordinal))
        {
            Fail(field, "isEnum", $"{field} must be one of the following values: {string.Join(", ", oneOf)}");
            return string.Empty;
        }

        return value.GetValue<string>();
    }

    /// <summary>
    /// A required, non-empty array of objects (frozen @IsArray + @ArrayNotEmpty
    /// + @ValidateNested). Returns null when structurally invalid; per-item
    /// failures are the caller's to add via <see cref="AddFailure"/>.
    /// </summary>
    public JsonArray? RequiredObjectArray(string field)
    {
        _knownFields.Add(field);
        var value = _body[field];
        if (value is null || value.GetValueKind() != JsonValueKind.Array)
        {
            Fail(field, "isArray", $"{field} must be an array");
            Fail(field, "arrayNotEmpty", $"{field} should not be empty");
            return null;
        }

        var array = value.AsArray();
        if (array.Count == 0)
        {
            Fail(field, "arrayNotEmpty", $"{field} should not be empty");
            return null;
        }

        return array;
    }

    /// <summary>Records a failure for a nested field (e.g. "settings.0.key").</summary>
    public void AddFailure(string field, string key, string message) => Fail(field, key, message);

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

    /// <summary>
    /// A field constrained @IsNotEmpty + @IsEnum (frozen template category):
    /// a missing value fails both constraints, a wrong value fails isEnum.
    /// </summary>
    public string? RequiredEnumNotEmpty(string field, IReadOnlyList<string> oneOf)
    {
        _knownFields.Add(field);
        var value = _body[field];
        if (value is null || value.GetValueKind() == JsonValueKind.Null)
        {
            Fail(field, "isNotEmpty", $"{field} should not be empty");
            Fail(field, "isEnum", $"{field} must be one of the following values: {string.Join(", ", oneOf)}");
            return null;
        }

        if (value.GetValueKind() != JsonValueKind.String)
        {
            Fail(field, "isEnum", $"{field} must be one of the following values: {string.Join(", ", oneOf)}");
            return null;
        }

        var text = value.GetValue<string>();
        if (text.Length == 0)
        {
            Fail(field, "isNotEmpty", $"{field} should not be empty");
        }

        if (!oneOf.Contains(text, StringComparer.Ordinal))
        {
            Fail(field, "isEnum", $"{field} must be one of the following values: {string.Join(", ", oneOf)}");
            return null;
        }

        return text;
    }

    /// <summary>An optional boolean field (frozen @IsOptional + @IsBoolean).</summary>
    public bool? OptionalBool(string field)
    {
        _knownFields.Add(field);
        var value = _body[field];
        if (value is null)
        {
            return null;
        }

        var kind = value.GetValueKind();
        if (kind is not (JsonValueKind.True or JsonValueKind.False))
        {
            Fail(field, "isBoolean", $"{field} must be a boolean value");
            return null;
        }

        return kind == JsonValueKind.True;
    }

    /// <summary>A required object field (frozen @IsObject).</summary>
    public JsonObject? RequiredObject(string field)
    {
        _knownFields.Add(field);
        var value = _body[field];
        if (value is not JsonObject obj)
        {
            Fail(field, "isObject", $"{field} must be an object");
            return null;
        }

        return obj;
    }

    /// <summary>An optional object field (frozen @IsOptional + @IsObject).</summary>
    public JsonObject? OptionalObject(string field)
    {
        _knownFields.Add(field);
        var value = _body[field];
        if (value is null)
        {
            return null;
        }

        if (value is not JsonObject obj)
        {
            Fail(field, "isObject", $"{field} must be an object");
            return null;
        }

        return obj;
    }

    /// <summary>An optional email field (frozen @IsOptional + @IsEmail).</summary>
    public string? OptionalEmail(string field)
    {
        _knownFields.Add(field);
        var value = _body[field];
        if (value is null)
        {
            return null;
        }

        if (value.GetValueKind() != JsonValueKind.String || !LooksLikeEmail(value.GetValue<string>()))
        {
            Fail(field, "isEmail", $"{field} must be an email");
            return null;
        }

        return value.GetValue<string>();
    }

    /// <summary>An optional UUID string field (frozen @IsOptional + @IsUUID).</summary>
    public string? OptionalUuid(string field)
    {
        _knownFields.Add(field);
        var value = _body[field];
        if (value is null || value.GetValueKind() == JsonValueKind.Null)
        {
            return null;
        }

        if (value.GetValueKind() != JsonValueKind.String || !Guid.TryParse(value.GetValue<string>(), out _))
        {
            Fail(field, "isUuid", $"{field} must be a UUID");
            return null;
        }

        return value.GetValue<string>();
    }

    /// <summary>
    /// A required, non-empty array of email addresses (frozen @IsArray +
    /// @ArrayNotEmpty + @IsEmail each — a missing value fails all three).
    /// </summary>
    public IReadOnlyList<string>? RequiredEmailArray(string field)
    {
        _knownFields.Add(field);
        var value = _body[field];
        if (value is null || value.GetValueKind() != JsonValueKind.Array)
        {
            Fail(field, "isArray", $"{field} must be an array");
            Fail(field, "arrayNotEmpty", $"{field} should not be empty");
            Fail(field, "isEmail", $"each value in {field} must be an email");
            return null;
        }

        var array = value.AsArray();
        if (array.Count == 0)
        {
            Fail(field, "arrayNotEmpty", $"{field} should not be empty");
            return null;
        }

        var items = new List<string>();
        foreach (var item in array)
        {
            if (item is null
                || item.GetValueKind() != JsonValueKind.String
                || !LooksLikeEmail(item.GetValue<string>()))
            {
                Fail(field, "isEmail", $"each value in {field} must be an email");
                return null;
            }

            items.Add(item.GetValue<string>());
        }

        return items;
    }

    /// <summary>An optional array of UUIDs (frozen @IsOptional + @IsArray + @IsUUID each).</summary>
    public IReadOnlyList<string>? OptionalUuidArray(string field)
    {
        _knownFields.Add(field);
        var value = _body[field];
        if (value is null)
        {
            return null;
        }

        if (value.GetValueKind() != JsonValueKind.Array)
        {
            Fail(field, "isArray", $"{field} must be an array");
            Fail(field, "isUuid", $"each value in {field} must be a UUID");
            return null;
        }

        var items = new List<string>();
        foreach (var item in value.AsArray())
        {
            if (item is null
                || item.GetValueKind() != JsonValueKind.String
                || !Guid.TryParse(item.GetValue<string>(), out _))
            {
                Fail(field, "isUuid", $"each value in {field} must be a UUID");
                return null;
            }

            items.Add(item.GetValue<string>());
        }

        return items;
    }

    /// <summary>The raw node for a known field (nested-block validation is the caller's).</summary>
    public JsonNode? RawNode(string field)
    {
        _knownFields.Add(field);
        return _body[field];
    }

    /// <summary>True when the body carries the property (even as null).</summary>
    public bool Has(string field) => _body.ContainsKey(field);

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

    public void ThrowIfInvalid() => _failures.ThrowIfAny();

    private void Fail(string field, string key, string message) => _failures.Add(field, key, message);

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

/// <summary>
/// Query-string validation reproducing the frozen global ValidationPipe over
/// @Query() DTOs: class-validator constraint keys/messages, Type(() =&gt; Number)
/// coercion for numeric params, and whitelist enforcement.
/// </summary>
public sealed class QueryValidator
{
    private readonly IQueryCollection _query;
    private readonly HashSet<string> _knownParams = new(StringComparer.Ordinal);
    private readonly ValidationFailures _failures = new();

    private QueryValidator(IQueryCollection query) => _query = query;

    public static QueryValidator Read(HttpContext ctx) => new(ctx.Request.Query);

    /// <summary>An optional integer param (frozen @IsInt + @Min/@Max with Number coercion).</summary>
    public int? OptionalInt(string field, int? min = null, int? max = null)
    {
        _knownParams.Add(field);
        if (!_query.TryGetValue(field, out var values))
        {
            return null;
        }

        var text = values[^1] ?? string.Empty;
        if (!double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number)
            || double.IsNaN(number)
            || number != Math.Truncate(number)
            || number is > int.MaxValue or < int.MinValue)
        {
            Fail(field, "isInt", $"{field} must be an integer number");
            return null;
        }

        var value = (int)number;
        if (min is not null && value < min)
        {
            Fail(field, "min", $"{field} must not be less than {min}");
        }

        if (max is not null && value > max)
        {
            Fail(field, "max", $"{field} must not be greater than {max}");
        }

        return value;
    }

    /// <summary>An optional free-form string param (frozen @IsOptional + @IsString).</summary>
    public string? OptionalString(string field)
    {
        _knownParams.Add(field);
        return _query.TryGetValue(field, out var values) ? values[^1] : null;
    }

    /// <summary>A required enum param (frozen @IsNotEmpty + @IsEnum).</summary>
    public string RequiredEnum(string field, IReadOnlyList<string> oneOf)
    {
        _knownParams.Add(field);
        var text = _query.TryGetValue(field, out var values) ? values[^1] : null;
        if (string.IsNullOrEmpty(text))
        {
            Fail(field, "isNotEmpty", $"{field} should not be empty");
            Fail(field, "isEnum", $"{field} must be one of the following values: {string.Join(", ", oneOf)}");
            return string.Empty;
        }

        if (!oneOf.Contains(text, StringComparer.Ordinal))
        {
            Fail(field, "isEnum", $"{field} must be one of the following values: {string.Join(", ", oneOf)}");
            return string.Empty;
        }

        return text;
    }

    /// <summary>An optional enum param (frozen @IsOptional + @IsEnum).</summary>
    public string? OptionalEnum(string field, IReadOnlyList<string> oneOf)
    {
        _knownParams.Add(field);
        if (!_query.TryGetValue(field, out var values))
        {
            return null;
        }

        var text = values[^1] ?? string.Empty;
        if (!oneOf.Contains(text, StringComparer.Ordinal))
        {
            Fail(field, "isEnum", $"{field} must be one of the following values: {string.Join(", ", oneOf)}");
            return null;
        }

        return text;
    }

    /// <summary>An optional UUID param (frozen @IsOptional + @IsUUID).</summary>
    public Guid? OptionalUuid(string field)
    {
        _knownParams.Add(field);
        if (!_query.TryGetValue(field, out var values))
        {
            return null;
        }

        if (!Guid.TryParse(values[^1], out var id))
        {
            Fail(field, "isUuid", $"{field} must be a UUID");
            return null;
        }

        return id;
    }

    /// <summary>An optional email param (frozen @IsOptional + @IsEmail).</summary>
    public string? OptionalEmail(string field)
    {
        _knownParams.Add(field);
        if (!_query.TryGetValue(field, out var values))
        {
            return null;
        }

        var text = values[^1] ?? string.Empty;
        var at = text.IndexOf('@', StringComparison.Ordinal);
        if (at <= 0 || at == text.Length - 1 || !text[(at + 1)..].Contains('.', StringComparison.Ordinal))
        {
            Fail(field, "isEmail", $"{field} must be an email");
            return null;
        }

        return text;
    }

    /// <summary>An optional ISO-8601 date param (frozen @IsOptional + @IsDateString).</summary>
    public DateTimeOffset? OptionalDateString(string field)
    {
        _knownParams.Add(field);
        if (!_query.TryGetValue(field, out var values))
        {
            return null;
        }

        if (!DateTimeOffset.TryParse(
                values[^1],
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal,
                out var parsed))
        {
            Fail(field, "isDateString", $"{field} must be a valid ISO 8601 date string");
            return null;
        }

        return parsed;
    }

    /// <summary>The frozen forbidNonWhitelisted, applied to query params.</summary>
    public void RejectUnknownParams()
    {
        foreach (var parameter in _query)
        {
            if (!_knownParams.Contains(parameter.Key))
            {
                Fail(parameter.Key, "whitelistValidation", $"property {parameter.Key} should not exist");
            }
        }
    }

    public void ThrowIfInvalid() => _failures.ThrowIfAny();

    private void Fail(string field, string key, string message) => _failures.Add(field, key, message);
}

/// <summary>
/// Validation over a nested JSON object, reporting into the parent's failure
/// list under a path prefix — the frozen @ValidateNested surfaces.
/// </summary>
public sealed class NestedValidator
{
    private readonly JsonObject _node;
    private readonly string _prefix;
    private readonly ValidationFailures _failures;

    internal NestedValidator(JsonObject node, string pathPrefix, ValidationFailures failures)
    {
        _node = node;
        _prefix = pathPrefix;
        _failures = failures;
    }

    /// <summary>A required, non-empty string (frozen nested @IsNotEmpty: one constraint for every failure shape).</summary>
    public string NonEmptyString(string field)
    {
        var node = _node[field];
        if (node is null
            || node.GetValueKind() != JsonValueKind.String
            || node.GetValue<string>().Length == 0)
        {
            _failures.Add($"{_prefix}.{field}", "isNotEmpty", $"{_prefix}.{field} should not be empty");
            return string.Empty;
        }

        return node.GetValue<string>();
    }

    /// <summary>A required email (frozen nested @IsEmail — the '@' probe class-validator applied here).</summary>
    public string Email(string field)
    {
        var node = _node[field];
        var text = node?.GetValueKind() == JsonValueKind.String ? node.GetValue<string>() : null;
        if (text is null || !text.Contains('@', StringComparison.Ordinal))
        {
            _failures.Add($"{_prefix}.{field}", "isEmail", $"{_prefix}.{field} must be an email");
            return string.Empty;
        }

        return text;
    }

    /// <summary>A required enum (frozen nested @IsEnum: one constraint for every failure shape).</summary>
    public string Enum(string field, IReadOnlyList<string> oneOf)
    {
        var node = _node[field];
        var text = node?.GetValueKind() == JsonValueKind.String ? node.GetValue<string>() : null;
        if (text is null || !oneOf.Contains(text, StringComparer.Ordinal))
        {
            _failures.Add(
                $"{_prefix}.{field}", "isEnum",
                $"{_prefix}.{field} must be one of the following values: {string.Join(", ", oneOf)}");
            return string.Empty;
        }

        return text;
    }
}

/// <summary>Route-parameter parsing pinned to the two frozen id postures.</summary>
public static class RouteParams
{
    /// <summary>The frozen ParseUUIDPipe surface: a malformed id is a 400.</summary>
    public static Guid PipedUuid(string id) =>
        Guid.TryParse(id, out var parsed)
            ? parsed
            : throw ApiException.BadRequest("Validation failed (uuid is expected)");

    /// <summary>
    /// The frozen no-pipe routes: a malformed id reached PostgreSQL, whose
    /// uuid-cast failure surfaced as a bare 500. Pinned.
    /// </summary>
    public static Guid UnpipedUuid(string id) =>
        Guid.TryParse(id, out var parsed)
            ? parsed
            : throw new ApiException(500, "Internal server error");
}
