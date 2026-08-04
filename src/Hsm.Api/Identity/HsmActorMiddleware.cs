using Hsm.Application.Abstractions;
using Hsm.Application.Auth;

namespace Hsm.Api.Identity;

/// <summary>
/// Publishes the authenticated caller as the request's actor, once, for every
/// route. It replaces the per-endpoint <c>RequestAuth.GateAsync</c> call: an
/// endpoint can no longer forget to establish identity, because it never had
/// to remember. Failing to produce an actor is safe — the pipeline refuses any
/// non-anonymous request without one — so the only direction this can be wrong
/// in is "too strict".
/// </summary>
public static class HsmActorMiddleware
{
    internal const string ActorItem = "Hsm.RequestAuth.Actor";

    public static IApplicationBuilder UseHsmActor(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.Use(async (context, next) =>
        {
            if (context.User.Identity?.IsAuthenticated == true)
            {
                var factory = context.RequestServices.GetRequiredService<RequestActorFactory>();
                context.Items[ActorItem] =
                    await factory.CreateAsync(context.User, context.RequestAborted);
            }

            await next();
        });
    }

    /// <summary>The actor installed on this context, or null when none was.</summary>
    internal static RequestActor? InstalledActor(HttpContext? context) =>
        context is not null && context.Items.TryGetValue(ActorItem, out var actor)
            ? actor as RequestActor
            : null;
}
