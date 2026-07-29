using Hsm.Application.Abstractions;

namespace Hsm.Application.Errors;

/// <summary>
/// Stable machine-readable error codes carried on the error envelope
/// (`issue.code`). Values are contract — frozen at
/// packages/common/src/enums/api-error-code.enum.ts; never rename.
/// </summary>
public static class ApiErrorCode
{
    public const string Unauthorized = "COMMON.UNAUTHORIZED";
    public const string Forbidden = "COMMON.FORBIDDEN";
    public const string NotFound = "COMMON.NOT_FOUND";
    public const string Conflict = "COMMON.CONFLICT";
    public const string TooManyRequests = "COMMON.TOO_MANY_REQUESTS";
    public const string Validation = "COMMON.VALIDATION";
    public const string Internal = "COMMON.INTERNAL";
    public const string InvalidCredentials = "AUTH.INVALID_CREDENTIALS";
}

/// <summary>
/// An application-level failure that maps onto the frozen error envelope:
/// HTTP status + optional issue fields. The web layer renders it; handlers
/// throw it. Mirrors the observable surface of Nest's HttpException family.
/// </summary>
public class ApiException : Exception
{
    public ApiException(int statusCode, string? message = null, string? code = null, string? errorLabel = null)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
        ErrorLabel = errorLabel;
        IssueMessage = message;
    }

    public int StatusCode { get; }

    /// <summary>Explicit issue.code; when null the status-mapped fallback applies.</summary>
    public string? Code { get; }

    /// <summary>The issue.error label (e.g. "Bad Request", "TOKEN_EXPIRED").</summary>
    public string? ErrorLabel { get; }

    /// <summary>issue.message; null renders an envelope without a message.</summary>
    public string? IssueMessage { get; init; }

    /// <summary>
    /// Set only by <see cref="Validation"/>: per-field failures the frozen
    /// ValidationPipe envelope renders as issue.message (an array) and
    /// issue.errors (per-field constraint keys) instead of the plain
    /// IssueMessage string. Empty for every other factory.
    /// </summary>
    public IReadOnlyList<ValidationFailure> ValidationFailures { get; private init; } = [];

    public static ApiException Unauthorized(string? message = "Unauthorized", string? code = null, string? errorLabel = null) =>
        new(401, message, code, errorLabel);

    public static ApiException Forbidden(string message, string? errorLabel = "Forbidden") =>
        new(403, message, errorLabel: errorLabel);

    /// <summary>
    /// No caller-facing detail — used where the frozen contract expects only
    /// the status-mapped code (e.g. pipeline-level role/onboarding gates).
    /// </summary>
    public static ApiException Forbidden() => new(403, errorLabel: "Forbidden");

    public static ApiException BadRequest(string message, string? errorLabel = "Bad Request") =>
        new(400, message, errorLabel: errorLabel);

    public static ApiException NotFound(string message) =>
        new(404, message, errorLabel: "Not Found");

    /// <summary>
    /// The frozen per-account recovery limit threw a bare
    /// HttpException('Too Many Requests', 429), whose envelope carries ONLY
    /// the status-mapped code — no message. Preserved here.
    /// </summary>
    public static ApiException TooManyRequests() => new(429);

    /// <summary>
    /// A 400 whose envelope carries the frozen ValidationPipe shape: issue.code
    /// COMMON.VALIDATION, issue.message as a string array, and issue.errors as
    /// per-field machine-readable constraint keys — the same shape Hsm.Web's
    /// edge-level ApiValidationException renders (see ApiEnvelope.IssueFor).
    /// Thrown by ValidationBehavior when an IValidator reports failures. Kept
    /// as a plain ApiException (not a subclass) so callers can catch/assert on
    /// the base type uniformly.
    /// </summary>
    public static ApiException Validation(IReadOnlyList<ValidationFailure> failures) =>
        new(400, string.Join("; ", failures.Select(f => f.Message)), ApiErrorCode.Validation)
        {
            ValidationFailures = failures,
        };
}
