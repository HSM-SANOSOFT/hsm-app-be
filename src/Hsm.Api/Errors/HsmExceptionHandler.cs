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
