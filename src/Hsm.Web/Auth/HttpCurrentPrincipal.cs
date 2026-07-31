using Hsm.Application.Abstractions;

namespace Hsm.Web.Auth;

/// <summary>
/// This host's <see cref="ICurrentPrincipal"/>: who the pipeline authorizes
/// against for the current request or circuit. Two surfaces publish into it,
/// and it fails closed when neither did — an unauthenticated request reaches
/// <c>AuthorizationBehavior</c> with a null actor and is refused, so a
/// forgotten install can only ever be too strict, never too permissive.
///
/// <list type="bullet">
/// <item><b>HTTP</b> — <see cref="RequestAuth.InstallActorAsync"/> stores the
/// actor on <see cref="HttpContext.Items"/> after validating the access token.
/// Reading it back through <see cref="IHttpContextAccessor"/> rather than out
/// of a scoped field means the actor survives the request even when the
/// framework builds a second DI scope for it (status-code re-execution and
/// <c>createScopeForErrors</c> both do).</item>
/// <item><b>Blazor circuit</b> — a circuit has no HttpContext, so
/// <see cref="ShellActor"/> derives the actor from the circuit's
/// authentication state and publishes it through the scoped
/// <see cref="AmbientPrincipal"/>.</item>
/// </list>
///
/// The HTTP actor wins when both exist: on a statically rendered page an
/// HttpContext IS present, and the token the request authenticated with is
/// the more specific truth.
/// </summary>
public sealed class HttpCurrentPrincipal(
    IHttpContextAccessor httpContextAccessor,
    AmbientPrincipal ambientPrincipal) : ICurrentPrincipal
{
    public RequestActor? Actor =>
        RequestAuth.InstalledActor(httpContextAccessor.HttpContext) ?? ambientPrincipal.Actor;
}
