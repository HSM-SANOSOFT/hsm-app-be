using FluentValidation;
using Hsm.Application.Abstractions;
using Hsm.Application.Auth;
using Hsm.Application.Errors;
using Hsm.Application.Users.Commands.ChangeUserRole;
using Hsm.Application.Users.Queries.ListUsers;
using Hsm.Web.Auth;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Hsm.Api.Tests.Shell;

public sealed class PipelineAuthorizationFactory : ShellFactory
{
    protected override string DatabaseName => "hsm_pipeline_authorization_test";
}

/// <summary>
/// The pipeline is the ONLY authorizer (plan Task 15). Every edge gate is
/// gone — no <c>UiServiceGate</c>, no role argument on any endpoint — so the
/// proof that removing them did not open a hole has to be taken where no edge
/// exists at all: an in-process dispatch off a Blazor-style scope, straight
/// through <see cref="IDispatcher"/>.
///
/// A doctor session dispatching the admin-only <see cref="ListUsersQuery"/>
/// must be refused by <c>AuthorizationBehavior</c> and nothing else. The
/// admin case is asserted alongside it so the refusal cannot be a blanket
/// "in-process dispatch never works".
/// </summary>
public sealed class PipelineAuthorizationTests(PipelineAuthorizationFactory factory)
    : ShellTest<PipelineAuthorizationFactory>(factory), IClassFixture<PipelineAuthorizationFactory>
{
    [Fact]
    public async Task Wrong_role_in_process_dispatch_is_refused_with_no_edge_check_present()
    {
        var doctorId = await Factory.SeedUserAsync(
            Unique("pipeline_doctor"), "Contract-Passw0rd", "doctor", DateTimeOffset.UtcNow);

        using var scope = await SignedInScopeAsync(doctorId, "doctor");
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        // The pipeline's refusal: a message-less 403 (new ForbiddenException()).
        // A 401 here would mean the actor never reached the pipeline, and the
        // test would be passing for the wrong reason.
        await Assert.ThrowsAsync<ForbiddenException>(
            () => dispatcher.Send(new ListUsersQuery(1, 10), CancellationToken.None));
    }

    [Fact]
    public async Task Admin_in_process_dispatch_is_allowed_by_the_same_pipeline()
    {
        var adminId = await Factory.SeedUserAsync(
            Unique("pipeline_admin"), "Contract-Passw0rd", "admin", DateTimeOffset.UtcNow);

        using var scope = await SignedInScopeAsync(adminId, "admin");
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        var page = await dispatcher.Send(new ListUsersQuery(1, 10), CancellationToken.None);

        Assert.Contains(page.Items, row => row.User.Id == adminId);
    }

    [Fact]
    public async Task Anonymous_in_process_dispatch_is_401_not_a_silent_pass()
    {
        using var scope = Factory.Services.CreateScope();
        // No authentication state set: nothing installs an actor, and the
        // pipeline must refuse rather than fall through to the handler.
        await scope.ServiceProvider.GetRequiredService<ShellActor>().InstallAsync(CancellationToken.None);
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        await Assert.ThrowsAsync<UnauthorizedException>(
            () => dispatcher.Send(new ListUsersQuery(1, 10), CancellationToken.None));
    }

    [Fact]
    public async Task Unknown_role_in_process_dispatch_is_a_validation_failure()
    {
        // Regression: ChangeUserRoleHandler's temporary inline guard (kept
        // until Task 3's ChangeUserRoleValidator exists). The HTTP door
        // already restricts the role param to RoleCatalog.All, so this proves
        // the HANDLER itself refuses an unknown role for callers that dispatch
        // the command directly — an in-process caller with no edge in front of
        // it, same posture as this suite's other pipeline-only proofs.
        var adminId = await Factory.SeedUserAsync(
            Unique("pipeline_admin_role"), "Contract-Passw0rd", "admin", DateTimeOffset.UtcNow);
        var targetId = await Factory.SeedUserAsync(
            Unique("pipeline_target"), "Contract-Passw0rd", "doctor", DateTimeOffset.UtcNow);

        using var scope = await SignedInScopeAsync(adminId, "admin");
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        await Assert.ThrowsAsync<ValidationException>(() => dispatcher.Send(
            new ChangeUserRoleCommand(targetId, "not-a-real-role"), CancellationToken.None));
    }

    /// <summary>
    /// A scope carrying an authenticated session exactly the way the shell
    /// does: the validated principal lands in the scoped authentication state
    /// provider, and <see cref="ShellActor"/> derives the actor from it.
    /// </summary>
    private async Task<IServiceScope> SignedInScopeAsync(Guid userId, string role)
    {
        var scope = Factory.Services.CreateScope();
        var principal = new AuthPrincipal
        {
            Id = userId.ToString(),
            Username = $"pipeline_{role}",
            Roles = [role],
            OnboardingCompletedAt = DateTimeOffset.UtcNow.ToString("O"),
            HasOnboardingClaim = true,
        };
        scope.ServiceProvider
            .GetRequiredService<HsmAuthenticationStateProvider>()
            .SetAuthenticationState(Task.FromResult(new AuthenticationState(
                principal.ToClaimsPrincipal())));
        var actor = await scope.ServiceProvider
            .GetRequiredService<ShellActor>()
            .InstallAsync(CancellationToken.None);

        // The principal really is installed — so a refusal below is a policy
        // decision, not a missing actor.
        Assert.NotNull(actor);
        Assert.Equal(userId.ToString(), actor.Id);
        Assert.Contains(role, actor.Roles);
        return scope;
    }
}
