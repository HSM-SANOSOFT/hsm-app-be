using System.Reflection;
using Hsm.Application.Abstractions;
using Hsm.Application.Docs.Commands.DeleteDocument;
using Hsm.Application.Docs.Commands.GenerateDocument;
using Hsm.Application.Docs.Commands.RenderDocument;
using Hsm.Application.Docs.Commands.UploadDocuments;
using Hsm.Application.Docs.Queries.GetDocument;
using Hsm.Application.Docs.Queries.GetDocumentUrl;
using Hsm.Application.Docs.Queries.ListDocuments;
using Hsm.Application.Docs.Queries.PresignDocuments;

namespace Hsm.Tests.Docs;

/// <summary>
/// Pins the Docs module's request policies directly on the request types.
///
/// The frozen <c>docs.controller.ts</c> decorates all nine <c>/v1/docs</c>
/// operations with a bare <c>@Roles()</c> — authenticated, onboarded, any
/// role. Confirmed against <c>Hsm.Api.Docs.DocsEndpoints</c>'s own doc
/// comment ("Every route is @Roles() with no arguments") and every
/// <c>RequestAuth.GateAsync(ctx)</c> call in that file (zero role arguments
/// anywhere), plus the absence of any 403/role-message assertion across
/// <c>tests/Hsm.Contract.Tests/Docs/*.cs</c> (the one <c>Forbidden</c>
/// assertion there is an expired presigned-URL grant, unrelated to role
/// policy). The brief's table matches the frozen behavior exactly — no
/// divergence to fix here, unlike Task 11's Templates finding.
///
/// <see cref="RenderDocumentCommand"/> carries <see cref="JobNameAttribute"/>
/// for Task 19's job-name → type registry. Its policy attribute is intent,
/// not enforcement today: <c>DocsJobProcessor.RunAsync</c> resolves and
/// invokes its handler directly, bypassing <c>IDispatcher</c>/the pipeline —
/// the same FAILED-status/retry reasoning as Coms's send-email job (see that
/// command's doc comment and <c>ComsJobProcessor</c>'s doc comment): on a
/// render failure this handler persists the document's FAILED status and
/// then re-throws so the channel processor's retry loop can count the
/// attempt, and <c>TransactionBehavior</c> would roll that FAILED write back
/// on the very re-throw that is supposed to carry it forward.
/// </summary>
public class DocsRequestPolicyTests
{
    public static TheoryData<Type> AuthenticatedRequests => new(
        typeof(ListDocumentsQuery),
        typeof(GetDocumentQuery),
        typeof(GetDocumentUrlQuery),
        typeof(PresignDocumentsQuery),
        typeof(GenerateDocumentCommand),
        typeof(UploadDocumentsCommand),
        typeof(DeleteDocumentCommand));

    [Theory]
    [MemberData(nameof(AuthenticatedRequests))]
    public void Every_http_facing_docs_request_requires_only_authentication(Type request)
    {
        Assert.NotNull(request);
        Assert.Null(request.GetCustomAttribute<RequireRoleAttribute>());
        Assert.Null(request.GetCustomAttribute<AllowAnonymousRequestAttribute>());
    }

    [Fact]
    public void Rendering_a_document_job_carries_its_frozen_queue_name()
    {
        var attribute = typeof(RenderDocumentCommand).GetCustomAttribute<JobNameAttribute>();
        Assert.NotNull(attribute);
        Assert.Equal("docs.render", attribute!.Name);
    }
}
