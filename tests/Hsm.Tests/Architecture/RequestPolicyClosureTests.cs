using System.Reflection;
using Hsm.Application.Abstractions;
using Hsm.Application.Coms.Commands.DispatchEmailBatch;
using Hsm.Application.Docs.Commands.RenderDocument;

namespace Hsm.Tests.Architecture;

/// <summary>
/// The per-module <c>*RequestPolicyTests</c> pin one slice each; nothing pins
/// the WHOLE closure. This test enumerates every non-abstract <see cref="IRequest{TResult}"/>
/// implementor in <c>Hsm.Application</c> and snapshots its policy as a
/// "TypeName: policy" string, so a new slice or an edited policy fails here —
/// loudly, as a diff against <see cref="ExpectedPolicies"/> — rather than
/// silently leaving a request unaccounted for. "Authenticated" (no attribute)
/// is a valid, deliberate policy, not a gap: the point is that every one of the
/// 52 current types is consciously listed, not that all must carry an
/// attribute. It also pins the <see cref="NoAmbientTransactionAttribute"/>
/// carrier set, closing the Task 19 gap where that set was asserted per-module
/// but never as a whole.
/// </summary>
public class RequestPolicyClosureTests
{
    [Fact]
    public void No_request_carries_both_AllowAnonymousRequest_and_RequireRole()
    {
        var violations = RequestTypes()
            .Where(t => t.GetCustomAttribute<AllowAnonymousRequestAttribute>() is not null
                     && t.GetCustomAttribute<RequireRoleAttribute>() is not null)
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void The_NoAmbientTransaction_carrier_set_is_exactly_the_two_known_job_commands()
    {
        var carriers = RequestTypes()
            .Where(t => t.GetCustomAttribute<NoAmbientTransactionAttribute>() is not null)
            .Select(t => t.Name)
            .OrderBy(n => n, StringComparer.Ordinal);

        Assert.Equal(
            new[] { nameof(DispatchEmailBatchCommand), nameof(RenderDocumentCommand) }
                .OrderBy(n => n, StringComparer.Ordinal),
            carriers);
    }

    [Fact]
    public void Every_request_types_policy_matches_the_pinned_snapshot()
    {
        var actual = RequestTypes()
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .Select(t => $"{t.Name}: {PolicyOf(t)}")
            .ToList();

        Assert.Equal(ExpectedPolicies, actual);
    }

    private static string PolicyOf(Type request) => request.GetCustomAttribute<AllowAnonymousRequestAttribute>() is not null
        ? "AllowAnonymousRequest"
        : request.GetCustomAttribute<RequireRoleAttribute>() is { } role
            ? $"RequireRole({string.Join(",", role.Roles)})"
            : request.GetCustomAttribute<AllowPendingOnboardingAttribute>() is not null
                ? "AllowPendingOnboarding"
                : "Authenticated";

    /// <summary>Every non-abstract type in Hsm.Application implementing some closed <c>IRequest&lt;T&gt;</c>.</summary>
    private static IEnumerable<Type> RequestTypes() => typeof(IRequest<>).Assembly
        .GetTypes()
        .Where(t => t is { IsClass: true, IsAbstract: false } && t.GetInterfaces().Any(
            i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>)));

    /// <summary>
    /// The pinned closure, sorted by type name. Edit this list only as a
    /// conscious decision — adding a slice, or deliberately changing a policy —
    /// never to silence a failing build.
    /// </summary>
    private static readonly string[] ExpectedPolicies =
    [
        "ChangeOwnPasswordCommand: Authenticated",
        "ChangeUserRoleCommand: RequireRole(admin)",
        "CompleteOnboardingCommand: AllowPendingOnboarding",
        "CreatePatientCommand: RequireRole(doctor,nurse,technician,therapist,pharmacist,admin)",
        "CreateStaffUserCommand: RequireRole(admin)",
        "CreateTemplateCommand: Authenticated",
        "DeleteDocumentCommand: Authenticated",
        "DeleteTemplateCommand: Authenticated",
        "DispatchEmailBatchCommand: Authenticated",
        "DraftRenderQuery: Authenticated",
        "ForgotPasswordCommand: AllowAnonymousRequest",
        "GenerateDocumentCommand: Authenticated",
        "GeneratePinCommand: Authenticated",
        "GetDocumentQuery: Authenticated",
        "GetDocumentUrlQuery: Authenticated",
        "GetEmailBatchQuery: Authenticated",
        "GetEmailRecipientQuery: Authenticated",
        "GetPatientQuery: RequireRole(doctor,nurse,technician,therapist,pharmacist,admin)",
        "GetSettingsQuery: RequireRole(admin)",
        "GetSystemStatusQuery: AllowAnonymousRequest",
        "GetTemplateQuery: Authenticated",
        "GetUserQuery: RequireRole(admin)",
        "IssueIntegrationTokensCommand: RequireRole(admin)",
        "ListDocumentsQuery: Authenticated",
        "ListEmailsQuery: Authenticated",
        "ListIntegrationAccountsQuery: RequireRole(admin)",
        "ListSettingsAuditQuery: RequireRole(admin)",
        "ListTemplatesQuery: Authenticated",
        "ListUsersQuery: RequireRole(admin)",
        "LoginCommand: AllowAnonymousRequest",
        "LogoutCommand: AllowAnonymousRequest",
        "LogoutIntegrationCommand: RequireRole(admin)",
        "PresignDocumentsQuery: Authenticated",
        "ProcessWebhookEventCommand: AllowAnonymousRequest",
        "ReceiveWebhookCommand: AllowAnonymousRequest",
        "RecoverUsernameCommand: AllowAnonymousRequest",
        "RefreshTokensCommand: AllowAnonymousRequest",
        "RenderDocumentCommand: Authenticated",
        "ResendEmailBatchCommand: Authenticated",
        "ResendEmailRecipientCommand: Authenticated",
        "ResetPasswordCommand: AllowAnonymousRequest",
        "RevokeIntegrationTokensCommand: RequireRole(admin)",
        "SearchPatientsQuery: RequireRole(doctor,nurse,technician,therapist,pharmacist,admin)",
        "SendEmailCommand: Authenticated",
        "SignupCommand: AllowAnonymousRequest",
        "SignupIntegrationCommand: RequireRole(admin)",
        "UpdateOwnProfileCommand: Authenticated",
        "UpdateSettingsCommand: RequireRole(admin)",
        "UpdateTemplateCommand: Authenticated",
        "UploadDocumentsCommand: Authenticated",
        "ValidatePinCommand: Authenticated",
        "ValidateTemplateQuery: Authenticated",
    ];
}
