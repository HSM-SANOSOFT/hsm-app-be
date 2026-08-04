using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;

namespace Hsm.Api.Http;

/// <summary>
/// The one place a caller-supplied request body is read as JSON, across the
/// six endpoint files Task 3 swept off <c>BodyValidator</c>. Wraps
/// <c>HttpRequest.ReadFromJsonAsync&lt;T&gt;</c>, converting a malformed or
/// empty body's <see cref="JsonException"/> into a
/// <see cref="ValidationException"/> — the same 400 vehicle every other shape
/// rule renders through — right at the untrusted-input boundary.
///
/// <para>Deliberately NOT a <see cref="Hsm.Api.Errors.HsmExceptionHandler"/>
/// arm: that switch sees every exception on every request, including
/// <see cref="JsonException"/>s thrown by handlers parsing STORED data (a
/// template's saved schema, a batch's saved data JSON) — those must stay a
/// logged 500, because a corrupted database row is our bug, not the caller's.
/// Catching it here instead means only a body actually read off THIS
/// request's stream gets reclassified as caller-malformed.</para>
///
/// <para>A wrong Content-Type (<see cref="InvalidOperationException"/> from
/// the same underlying call) is deliberately left uncaught here too — it
/// falls through to the default 500, consistent with
/// <c>DocsEndpoints.ReadUploadPayload</c>'s existing precedent of treating a
/// stray <see cref="InvalidOperationException"/> from a body-reading path as
/// our bug rather than the caller's.</para>
/// </summary>
public static class RequestJsonReader
{
    public static async Task<T?> ReadValidatedJsonAsync<T>(this HttpRequest request, CancellationToken ct)
    {
        try
        {
            return await request.ReadFromJsonAsync<T>(ct);
        }
        catch (JsonException)
        {
            throw new ValidationException(
                [new ValidationFailure("body", "Request body is not valid JSON.")]);
        }
    }
}
