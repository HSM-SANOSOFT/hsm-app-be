using FluentValidation;
using Hsm.Application.Errors;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Hsm.Api.Errors;

/// <summary>
/// The ONE place an exception becomes a response on this door. Every failure
/// renders RFC 9457 problem+json; `traceId` is attached centrally by
/// AddProblemDetails' CustomizeProblemDetails, so no branch here can forget it.
///
/// <para>Only <see cref="HsmException"/> and FluentValidation's
/// <see cref="ValidationException"/> carry caller-facing text. Everything else
/// is a bug: it is logged with its full detail and answered with a bare 500,
/// because an exception message from an unplanned path is exactly the kind of
/// thing that leaks a connection string or a row's contents.</para>
///
/// <para>This is a global, catch-all switch — it sees exceptions from
/// EVERY request, not just caller-supplied bodies. That is exactly why a
/// malformed-request-body <c>System.Text.Json.JsonException</c> is NOT given
/// an arm here (Task 3's fix round reverted an earlier attempt to do so):
/// several handlers also call <c>JsonNode.Parse</c>/<c>JsonSerializer</c>
/// against STORED data (e.g. <c>ValidateTemplateHandler</c>,
/// <c>SendEmailHandler</c> parsing a template's stored schema/data JSON), and
/// a `JsonException` from corrupted stored data must stay a logged 500, not a
/// caller-facing 400 that hides a server-side data bug. The distinction is
/// WHERE a body-read JsonException is caught — at each of the six swept
/// endpoints' <c>ReadFromJsonAsync</c> call site, via
/// <see cref="Hsm.Api.Http.RequestJsonReader.ReadValidatedJsonAsync{T}"/> —
/// not what type it is globally.</para>
///
/// <para>Task 5's resource endpoints bind their request record straight from
/// the body instead (minimal API's own inferred-body-parameter binding), so
/// they have no <c>ReadValidatedJsonAsync</c> call site to catch at. A
/// malformed body there fails inside the framework's own binding step and
/// surfaces as <see cref="Microsoft.AspNetCore.Http.BadHttpRequestException"/>,
/// which — unlike bare <c>JsonException</c> — can only originate from THIS
/// request's route/query/body binding, so it DOES get its own arm below.</para>
/// </summary>
public sealed partial class HsmExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<HsmExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var problem = Map(exception);
        var status = problem.Status ?? StatusCodes.Status500InternalServerError;
        if (status >= StatusCodes.Status500InternalServerError)
        {
            LogUnhandled(logger, httpContext.Request.Method, httpContext.Request.Path, exception);
        }

        httpContext.Response.StatusCode = status;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problem,
        });
    }

    /// <summary>The closed set, and nothing else, decides a status code.</summary>
    internal static ProblemDetails Map(Exception exception) => exception switch
    {
        ValidationException validation => ValidationProblem(validation),
        UnauthorizedException => Problem(StatusCodes.Status401Unauthorized, "Unauthorized"),
        ForbiddenException => Problem(StatusCodes.Status403Forbidden, "Forbidden"),
        NotFoundException notFound =>
            Problem(StatusCodes.Status404NotFound, "Not Found", notFound.Message),
        ConflictException conflict =>
            Problem(StatusCodes.Status409Conflict, "Conflict", conflict.Message),
        TooManyRequestsException => Problem(StatusCodes.Status429TooManyRequests, "Too Many Requests"),
        // Unlike the JsonException this type deliberately has no arm here (see
        // the class doc), BadHttpRequestException can ONLY come from THIS
        // request's own route/query/body binding — a handler parsing STORED
        // JSON throws plain JsonException, never this — so reclassifying it
        // as caller-facing (via its own framework-assigned StatusCode, always
        // 400 for a malformed body under Task 5's inferred-body-parameter
        // endpoints) cannot mask a server-side data bug as a 500.
        Microsoft.AspNetCore.Http.BadHttpRequestException badRequest =>
            Problem(badRequest.StatusCode, "Bad Request"),
        _ => Problem(StatusCodes.Status500InternalServerError, "Internal Server Error"),
    };

    /// <summary>
    /// ValidationProblemDetails with camelCase keys, because the wire is
    /// camelCase everywhere else and a client should not have to know that
    /// validators name C# properties.
    /// </summary>
    internal static ProblemDetails ValidationProblem(ValidationException exception)
    {
        var errors = exception.Errors
            .GroupBy(failure => CamelCasePath(failure.PropertyName), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(failure => failure.ErrorMessage).ToArray(),
                StringComparer.Ordinal);

        return new ValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more validation errors occurred.",
        };
    }

    /// <summary>`Files[0].FileName` becomes `files[0].fileName`.</summary>
    internal static string CamelCasePath(string propertyName) =>
        string.IsNullOrEmpty(propertyName)
            ? propertyName
            : string.Join('.', propertyName.Split('.').Select(CamelCaseSegment));

    private static string CamelCaseSegment(string segment) =>
        segment.Length == 0 || char.IsLower(segment[0])
            ? segment
            : char.ToLowerInvariant(segment[0]) + segment[1..];

    /// <summary>
    /// Title and Type are left to ProblemDetailsDefaults where we do not set
    /// them; it fills the RFC 9110 status URI and the canonical reason phrase.
    /// </summary>
    private static ProblemDetails Problem(int status, string title, string? detail = null) => new()
    {
        Status = status,
        Title = title,
        Detail = detail,
    };

    [LoggerMessage(
        EventId = 5000,
        Level = LogLevel.Error,
        Message = "Unhandled failure serving {Method} {Path}.")]
    private static partial void LogUnhandled(
        ILogger logger, string method, string path, Exception exception);
}
