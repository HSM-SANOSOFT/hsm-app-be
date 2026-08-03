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
/// <see cref="RenderDocumentCommand"/> carries <see cref="JobNameAttribute"/>,
/// which the job registry maps to and from, and its policy IS enforced since
/// Task 19: the queue consumer dispatches it through <c>IDispatcher</c> with
/// the actor the job was enqueued with. It also carries
/// <see cref="NoAmbientTransactionAttribute"/> — pinned below because it is a
/// deliberate, narrow exception to "commands run in a transaction": on a render
/// failure the handler persists the document's FAILED status (and
/// <c>TemplateParser</c> its parse-log row) and then re-throws so the queue
/// counts the attempt, and a pipeline-owned transaction would roll both back on
/// the very re-throw meant to carry them forward.
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

    [Fact]
    public void Rendering_a_document_job_owns_its_own_commits()
    {
        // The FAILED status and the parse-log row are written on the way out of
        // a failing attempt and must survive its re-throw — and a transaction
        // has no business staying open across a PDF render and an S3 upload.
        Assert.NotNull(
            typeof(RenderDocumentCommand).GetCustomAttribute<NoAmbientTransactionAttribute>());
    }
}
