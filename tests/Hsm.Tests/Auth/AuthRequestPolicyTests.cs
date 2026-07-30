using System.Reflection;
using Hsm.Application.Abstractions;
using Hsm.Application.Auth.Commands.CompleteOnboarding;
using Hsm.Application.Auth.Commands.ForgotPassword;
using Hsm.Application.Auth.Commands.IssueIntegrationTokens;
using Hsm.Application.Auth.Commands.Login;
using Hsm.Application.Auth.Commands.Logout;
using Hsm.Application.Auth.Commands.LogoutIntegration;
using Hsm.Application.Auth.Commands.RecoverUsername;
using Hsm.Application.Auth.Commands.RefreshTokens;
using Hsm.Application.Auth.Commands.ResetPassword;
using Hsm.Application.Auth.Commands.RevokeIntegrationTokens;
using Hsm.Application.Auth.Commands.Signup;
using Hsm.Application.Auth.Commands.SignupIntegration;
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
        typeof(SignupCommand),
        typeof(RefreshTokensCommand),
        typeof(LogoutCommand),
        typeof(ForgotPasswordCommand),
        typeof(ResetPasswordCommand),
        typeof(RecoverUsernameCommand));

    public static TheoryData<Type> AdminOnlyRequests => new(
        typeof(SignupIntegrationCommand),
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
    public void Signing_out_never_depends_on_a_usable_session()
    {
        // The regression this guards: making LogoutCommand "authenticated"
        // strands anyone whose access token has expired — the pipeline refuses
        // before the handler, which is the only code that tolerates expiry.
        Assert.NotNull(typeof(LogoutCommand).GetCustomAttribute<AllowAnonymousRequestAttribute>());
        Assert.Null(typeof(LogoutCommand).GetCustomAttribute<RequireRoleAttribute>());
    }

    [Fact]
    public void Integration_sign_out_requires_a_principal_and_a_completed_onboarding()
    {
        // No pending exemption here: unlike LogoutCommand this one is a
        // machine-account administration call, not a self-service escape hatch.
        Assert.Null(
            typeof(LogoutIntegrationCommand).GetCustomAttribute<AllowAnonymousRequestAttribute>());
        Assert.Null(
            typeof(LogoutIntegrationCommand).GetCustomAttribute<AllowPendingOnboardingAttribute>());
        Assert.Null(typeof(LogoutIntegrationCommand).GetCustomAttribute<RequireRoleAttribute>());
    }
}
