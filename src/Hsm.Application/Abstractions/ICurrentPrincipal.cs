namespace Hsm.Application.Abstractions;

/// <summary>Who is acting. Supplied by the host: HTTP context, Blazor circuit, or queue envelope.</summary>
public interface ICurrentPrincipal
{
    RequestActor? Actor { get; }
}

/// <summary>The acting identity as the pipeline needs it.</summary>
public sealed record RequestActor(string Id, IReadOnlyList<string> Roles, bool OnboardingCompleted)
{
    public bool IsInRole(string role) => Roles.Contains(role);
}

/// <summary>
/// Scoped, settable principal. The worker installs the actor captured when the
/// job was enqueued, so a queued command authorizes exactly as its HTTP
/// counterpart did.
/// </summary>
public sealed class AmbientPrincipal : ICurrentPrincipal
{
    public RequestActor? Actor { get; private set; }
    public void Set(RequestActor? actor) => Actor = actor;
}
