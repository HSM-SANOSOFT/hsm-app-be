using Hsm.Application.Abstractions;
using Hsm.Application.Abstractions.Behaviors;
using Hsm.Application.Errors;

namespace Hsm.Tests.Abstractions;

public class AuthorizationBehaviorTests
{
    [AllowAnonymousRequest]
    private sealed record Public : ICommand<string>;

    private sealed record NeedsAuth : ICommand<string>;

    [RequireRole("admin")]
    private sealed record NeedsAdmin : ICommand<string>;

    [RequireRole("admin")]
    [AllowPendingOnboarding]
    private sealed record AdminDuringOnboarding : ICommand<string>;

    private static AuthorizationBehavior<TRequest, string> Behavior<TRequest>(RequestActor? actor)
        where TRequest : IRequest<string>
    {
        var ambient = new AmbientPrincipal();
        ambient.Set(actor);
        return new AuthorizationBehavior<TRequest, string>(ambient);
    }

    private static RequestHandlerDelegate<string> Reached(out Func<bool> wasReached)
    {
        var hit = false;
        wasReached = () => hit;
        return () => { hit = true; return Task.FromResult("ok"); };
    }

    [Fact]
    public async Task Anonymous_request_runs_without_a_principal()
    {
        var next = Reached(out var reached);
        var result = await Behavior<Public>(actor: null)
            .HandleAsync(new Public(), next, CancellationToken.None);

        Assert.Equal("ok", result);
        Assert.True(reached());
    }

    [Fact]
    public async Task Unauthenticated_request_is_rejected_before_the_handler()
    {
        var next = Reached(out var reached);
        await Assert.ThrowsAsync<UnauthorizedException>(() => Behavior<NeedsAuth>(actor: null)
            .HandleAsync(new NeedsAuth(), next, CancellationToken.None));

        Assert.False(reached());
    }

    [Fact]
    public async Task Wrong_role_is_forbidden_before_the_handler()
    {
        var doctor = new RequestActor("u1", ["doctor"], OnboardingCompleted: true);
        var next = Reached(out var reached);

        await Assert.ThrowsAsync<ForbiddenException>(() => Behavior<NeedsAdmin>(doctor)
            .HandleAsync(new NeedsAdmin(), next, CancellationToken.None));

        Assert.False(reached());
    }

    [Fact]
    public async Task Required_role_present_runs()
    {
        var admin = new RequestActor("u1", ["admin"], OnboardingCompleted: true);
        var next = Reached(out var reached);

        await Behavior<NeedsAdmin>(admin)
            .HandleAsync(new NeedsAdmin(), next, CancellationToken.None);

        Assert.True(reached());
    }

    [Fact]
    public async Task Pending_onboarding_is_rejected_unless_the_request_opts_in()
    {
        var pending = new RequestActor("u1", ["admin"], OnboardingCompleted: false);

        await Assert.ThrowsAsync<ForbiddenException>(() => Behavior<NeedsAdmin>(pending)
            .HandleAsync(new NeedsAdmin(), () => Task.FromResult("ok"),
                CancellationToken.None));

        await Behavior<AdminDuringOnboarding>(pending).HandleAsync(
            new AdminDuringOnboarding(), () => Task.FromResult("ok"),
            CancellationToken.None);
    }
}
