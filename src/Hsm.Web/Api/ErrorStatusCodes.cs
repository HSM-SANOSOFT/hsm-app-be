using Hsm.Application.Errors;

namespace Hsm.Web.Api;

/// <summary>
/// The single status-code classification behind BOTH frozen error surfaces:
/// the envelope's issue.code fallback (response.filter.ts) and the FHIR
/// OperationOutcome issue code (fhir-operation-outcome.filter.ts).
/// </summary>
public static class ErrorStatusCodes
{
    public static (string EnvelopeCode, string FhirIssueCode) For(int status) => status switch
    {
        401 => (ApiErrorCode.Unauthorized, "forbidden"),
        403 => (ApiErrorCode.Forbidden, "forbidden"),
        404 => (ApiErrorCode.NotFound, "not-found"),
        409 => (ApiErrorCode.Conflict, "duplicate"),
        429 => (ApiErrorCode.TooManyRequests, "processing"),
        400 or 422 => (ApiErrorCode.Validation, "invalid"),
        >= 500 => (ApiErrorCode.Internal, "exception"),
        _ => (ApiErrorCode.Internal, "processing"),
    };
}

/// <summary>
/// The shared try/catch shape of the two error surfaces: run the work, and
/// when it fails before the response has started, hand the failure to the
/// caller's renderer (envelope on /v1, OperationOutcome on FHIR). This is a
/// helper, not a rewiring — each surface keeps its own middleware/wrapper.
/// </summary>
public static class ApiErrorGuard
{
    public static async Task RunAsync(
        HttpContext ctx,
        Func<Task> work,
        Func<HttpContext, ApiException, Task> renderApiError,
        Func<HttpContext, Task> renderUnhandledError)
    {
        try
        {
            await work();
        }
        catch (ApiException exception) when (!ctx.Response.HasStarted)
        {
            await renderApiError(ctx, exception);
        }
        catch (Exception) when (!ctx.Response.HasStarted)
        {
            await renderUnhandledError(ctx);
        }
    }
}
