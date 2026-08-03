namespace Hsm.Application.Errors;

/// <summary>
/// The root of every failure the application deliberately raises. One handler in
/// each transport maps this closed set onto a status code; anything NOT derived
/// from it is a bug and surfaces as a bare 500 with no caller-facing detail.
///
/// <para>There is deliberately no general-purpose "BadRequestException". A 400
/// is what a malformed request gets, and malformed requests are the validators'
/// business (FluentValidation, Task 3). A refusal that is not about the request's
/// shape is a Conflict, a Forbidden, or a bug — reintroducing a catch-all here
/// would rebuild the ApiException this type replaced.</para>
/// </summary>
public abstract class HsmException(string? message) : Exception(message);

/// <summary>No credential, or a credential that no longer authenticates. 401.</summary>
public sealed class UnauthorizedException(string? message = null) : HsmException(message);

/// <summary>Authenticated, but not permitted. 403.</summary>
public sealed class ForbiddenException(string? message = null) : HsmException(message);

/// <summary>
/// The addressed resource does not exist. 404. Carries the resource NAME and the
/// identifier separately so a caller can tell "no such document" from "no such
/// template" without parsing prose, and so the message is built one way everywhere.
/// </summary>
public sealed class NotFoundException(string resource, object id)
    : HsmException($"{resource} '{id}' was not found.")
{
    public string Resource { get; } = resource;

    public string Id { get; } = id?.ToString() ?? string.Empty;
}

/// <summary>
/// The request is well-formed but conflicts with the resource's current state:
/// a duplicate key, an already-completed onboarding, a template still in use. 409.
/// </summary>
public sealed class ConflictException(string message) : HsmException(message);

/// <summary>A per-account or per-caller quota is exhausted. 429.</summary>
public sealed class TooManyRequestsException(string? message = null) : HsmException(message);
