using System.Reflection;
using Hsm.Application.Abstractions;
using Hsm.Application.Auth.Commands.CompleteOnboarding;
using Hsm.Application.Auth.Commands.ForgotPassword;
using Hsm.Application.Auth.Commands.IssueIntegrationTokens;
using Hsm.Application.Auth.Commands.Login;
using Hsm.Application.Auth.Commands.LogoutIntegration;
using Hsm.Application.Auth.Commands.RecoverUsername;
using Hsm.Application.Auth.Commands.RefreshIntegrationTokens;
using Hsm.Application.Auth.Commands.ResetPassword;
using Hsm.Application.Auth.Commands.RevokeIntegrationTokens;
using Hsm.Application.Auth.Commands.Register;
using Hsm.Application.Auth.Commands.RegisterIntegration;
using Hsm.Application.Auth.Queries.GetMe;
using Hsm.Application.Auth.Queries.ListIntegrationAccounts;
using Hsm.Domain.Identity;

namespace Hsm.Tests.Auth;

/// <summary>
/// The auth module's policies are the one place in this codebase where a
/// mistake locks every user out (a missing <see cref="AllowAnonymousRequestAttribute"/>
/// on sign-in or password reset) or opens the admin surface to anyone (a
/// missing <see cref="RequireRoleAttribute"/>). They are asserted directly on
/// the request types so a rename or an accidental deletion fails here, in
/// milliseconds, rather than in the contract suite.
/// </summary>
public class AuthRequestPolicyTests
{
    public static TheoryData<Type> AnonymousRequests => new(
        typeof(LoginCommand),
        typeof(RegisterCommand),
        typeof(RefreshIntegrationTokensCommand),
        typeof(ForgotPasswordCommand),
        typeof(ResetPasswordCommand),
        typeof(RecoverUsernameCommand));

    public static TheoryData<Type> AdminOnlyRequests => new(
        typeof(RegisterIntegrationCommand),
        typeof(ListIntegrationAccountsQuery),
        typeof(IssueIntegrationTokensCommand),
        typeof(RevokeIntegrationTokensCommand));

    [Theory]
    [MemberData(nameof(AnonymousRequests))]
    public void Credential_bearing_requests_are_reachable_without_a_principal(Type request)
    {
        Assert.NotNull(request);
        Assert.NotNull(request.GetCustomAttribute<AllowAnonymousRequestAttribute>());
        // An anonymous request must never also carry a role requirement.
        Assert.Null(request.GetCustomAttribute<RequireRoleAttribute>());
    }

    [Theory]
    [MemberData(nameof(AdminOnlyRequests))]
    public void Integration_account_administration_is_admin_only(Type request)
    {
        Assert.NotNull(request);
        var attribute = request.GetCustomAttribute<RequireRoleAttribute>();

        Assert.NotNull(attribute);
        Assert.Contains(Roles.Admin, attribute!.Roles);
        Assert.Null(request.GetCustomAttribute<AllowAnonymousRequestAttribute>());
    }

    [Fact]
    public void Completing_onboarding_is_reachable_by_a_pending_user()
    {
        // The whole point of the route: a pending account must be able to stop
        // being pending. Authenticated, but exempt from the onboarding gate.
        Assert.NotNull(
            typeof(CompleteOnboardingCommand).GetCustomAttribute<AllowPendingOnboardingAttribute>());
        Assert.Null(
            typeof(CompleteOnboardingCommand).GetCustomAttribute<AllowAnonymousRequestAttribute>());
        Assert.Null(typeof(CompleteOnboardingCommand).GetCustomAttribute<RequireRoleAttribute>());
    }

    [Fact]
    public void Reading_your_own_account_is_reachable_by_a_pending_user()
    {
        // The mirror of the rule above, and the reason GetMeQuery exists as a
        // request type at all: a pending account has to be able to see that it
        // is pending. Without the exemption, the one read that tells a client
        // to show the onboarding screen would be refused by the onboarding
        // gate — and the shell would have no way to know why.
        Assert.NotNull(typeof(GetMeQuery).GetCustomAttribute<AllowPendingOnboardingAttribute>());
        Assert.Null(typeof(GetMeQuery).GetCustomAttribute<AllowAnonymousRequestAttribute>());
        Assert.Null(typeof(GetMeQuery).GetCustomAttribute<RequireRoleAttribute>());
    }

    [Fact]
    public void Integration_sign_out_requires_an_admin_with_a_completed_onboarding()
    {
        // No pending exemption here: unlike LogoutCommand this one is a
        // machine-account administration call, not a self-service escape hatch.
        Assert.Null(
            typeof(LogoutIntegrationCommand).GetCustomAttribute<AllowAnonymousRequestAttribute>());
        Assert.Null(
            typeof(LogoutIntegrationCommand).GetCustomAttribute<AllowPendingOnboardingAttribute>());

        // Task 15: the frozen @Roles(admin) moved off the edge and onto the
        // request. Without it, deleting the edge role check would have let any
        // authenticated caller sign out a machine account.
        var policy = typeof(LogoutIntegrationCommand).GetCustomAttribute<RequireRoleAttribute>();
        Assert.NotNull(policy);
        Assert.Equal([Roles.Admin], policy.Roles);
    }

    [Fact]
    public void Refreshing_a_token_is_reachable_without_a_principal_and_gives_no_privilege()
    {
        // Anonymous because the refresh TOKEN is the credential — the request is
        // reachable precisely when the access token is not. That makes the
        // absence of a role requirement load-bearing rather than incidental: a
        // [RequireRole] here could not run at all, since there is no principal
        // for the pipeline to weigh. What limits the command instead is that the
        // token is the ONLY thing identifying the caller, and only an
        // integration account has one.
        Assert.NotNull(
            typeof(RefreshIntegrationTokensCommand).GetCustomAttribute<AllowAnonymousRequestAttribute>());
        Assert.Null(
            typeof(RefreshIntegrationTokensCommand).GetCustomAttribute<RequireRoleAttribute>());
    }

    [Fact]
    public void Provisioning_an_integration_is_admin_only_and_never_anonymous()
    {
        // The most valuable thing this module hands out: a credential that
        // renews itself forever. It is covered by the theory above too, and
        // stated again here because the register ROUTE is anonymous-adjacent —
        // it sits beside POST /api/v1/identity/register, which is deliberately
        // open to the public — and a copy-paste of the wrong policy between
        // neighbours is exactly the mistake this pins.
        var policy = typeof(RegisterIntegrationCommand).GetCustomAttribute<RequireRoleAttribute>();
        Assert.NotNull(policy);
        Assert.Equal([Roles.Admin], policy.Roles);
        Assert.Null(
            typeof(RegisterIntegrationCommand).GetCustomAttribute<AllowAnonymousRequestAttribute>());
    }
}
