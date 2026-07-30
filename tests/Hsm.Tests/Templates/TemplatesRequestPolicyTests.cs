using System.Reflection;
using Hsm.Application.Abstractions;
using Hsm.Application.Templates.Commands.CreateTemplate;
using Hsm.Application.Templates.Commands.DeleteTemplate;
using Hsm.Application.Templates.Commands.UpdateTemplate;
using Hsm.Application.Templates.Queries.DraftRender;
using Hsm.Application.Templates.Queries.GetTemplate;
using Hsm.Application.Templates.Queries.ListTemplates;
using Hsm.Application.Templates.Queries.ValidateTemplate;

namespace Hsm.Tests.Templates;

/// <summary>
/// Pins the seven templates request types' authorization policy directly on
/// the request type, so a rename or an accidental attribute change fails here
/// in milliseconds rather than in the contract suite.
///
/// The frozen <c>templates.controller.ts</c> decorates all seven routes with
/// a bare <c>@Roles()</c> — no role list — which the frozen RolesGuard reads
/// as "authenticated only, any role passes". The task brief's conversion
/// table proposed <c>[RequireRole(Roles.Admin)]</c> for the three mutating
/// commands; that was proven wrong empirically (not assumed): applying it
/// made 19 of the 25 Templates contract tests fail with 403, because
/// <c>TemplatesContractTest.BearerAsync</c> — used by every CRUD test,
/// including every create/update/delete — seeds a "doctor" bearer by design,
/// per its own doc comment: "templates routes accept ANY authenticated,
/// onboarded role". All seven request types are therefore authenticated-only:
/// no <see cref="RequireRoleAttribute"/>, no <see cref="AllowAnonymousRequestAttribute"/>.
/// </summary>
public class TemplatesRequestPolicyTests
{
    public static TheoryData<Type> AuthenticatedRequests => new(
        typeof(ListTemplatesQuery),
        typeof(GetTemplateQuery),
        typeof(ValidateTemplateQuery),
        typeof(DraftRenderQuery),
        typeof(CreateTemplateCommand),
        typeof(UpdateTemplateCommand),
        typeof(DeleteTemplateCommand));

    [Theory]
    [MemberData(nameof(AuthenticatedRequests))]
    public void Every_templates_request_requires_only_authentication_no_role_and_no_anonymous_access(Type request)
    {
        Assert.NotNull(request);
        Assert.Null(request.GetCustomAttribute<RequireRoleAttribute>());
        Assert.Null(request.GetCustomAttribute<AllowAnonymousRequestAttribute>());
    }
}
