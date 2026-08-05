using Hsm.Api.Identity;
using Hsm.Application.Abstractions;

namespace Hsm.Api.Identity;

/// <summary>
/// This host's <see cref="ICurrentPrincipal"/>: who the pipeline authorizes
/// against for the current request. One publisher —
/// <see cref="HsmActorMiddleware.UseHsmActor"/>, which runs after
/// authentication, for every route — and it fails closed when authentication
/// produced nothing: the request reaches <c>AuthorizationBehavior</c> with a
/// null actor and is refused, so a missing actor can only ever be too strict,
/// never too permissive.
///
/// Reading the actor back through <see cref="IHttpContextAccessor"/> rather
/// than out of a scoped field is not decoration: it ties the actor to the
/// REQUEST rather than to a DI scope. ASP.NET Core builds a second scope
/// whenever a pipeline re-executes a request (status-code pages, error
/// handlers), and a scoped field does not survive that.
/// <see cref="HttpContext.Items"/> does.
///
/// There is deliberately no ambient fallback: this door has exactly one way
/// in. A fallback would hand an actor to a route that never asked for one —
/// the one direction of error that is a security bug rather than an outage.
/// The in-process door (<c>Hsm.Web</c>) publishes through
/// <c>AmbientPrincipal</c> instead, and each host registers only the source it
/// actually has.
/// </summary>
public sealed class HttpCurrentPrincipal(IHttpContextAccessor httpContextAccessor) : ICurrentPrincipal
{
    public RequestActor? Actor => HsmActorMiddleware.InstalledActor(httpContextAccessor.HttpContext);
}
