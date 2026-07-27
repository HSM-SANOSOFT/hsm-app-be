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

    public static ApiException Unauthorized(string? message = "Unauthorized", string? code = null, string? errorLabel = null) =>
        new(401, message, code, errorLabel);

    public static ApiException Forbidden(string message, string? errorLabel = "Forbidden") =>
        new(403, message, errorLabel: errorLabel);

    public static ApiException BadRequest(string message, string? errorLabel = "Bad Request") =>
        new(400, message, errorLabel: errorLabel);

    public static ApiException NotFound(string message) =>
        new(404, message, errorLabel: "Not Found");

    /// <summary>
    /// The frozen per-account recovery limit threw a bare
    /// HttpException('Too Many Requests', 429), whose envelope carries ONLY
    /// the status-mapped code — no message. Preserved here.
    /// </summary>
    public static ApiException TooManyRequests() =>
        new(429, message: null, code: null, errorLabel: null) { IssueMessage = null };
}
