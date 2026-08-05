using Hsm.Application.Abstractions;
using Hsm.Application.Errors;
using Hsm.Domain.Docs;

namespace Hsm.Application.Docs.Commands.GenerateDocument;

/// <summary>
/// <b>Does not enqueue the render job itself.</b>
/// <c>TransactionBehavior</c> now wraps this whole handler in
/// one transaction that commits only after <c>HandleAsync</c> returns, so
/// enqueuing here would race the commit: the consumer runs in another process
/// and could try to render a document it cannot see yet. The job id IS minted
/// here (no I/O — safe before commit) so it travels back with the document id;
/// <c>DocsEndpoints.GenerateDocument</c> enqueues after
/// <c>dispatcher.Send</c> returns, which is a real post-commit point. See
/// <c>SendEmailHandler</c>'s doc comment (Task 12) for the contract-test
/// failure that proved this fix necessary, not merely theoretical.
/// </summary>
public sealed class GenerateDocumentHandler(IDocumentStore store, ICurrentPrincipal principal)
    : IRequestHandler<GenerateDocumentCommand, GenerateDocumentResult>
{
    public async Task<GenerateDocumentResult> HandleAsync(GenerateDocumentCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = principal.Actor ?? throw new UnauthorizedException();
        var userId = Guid.Parse(actor.Id);

        // Minted (no I/O), not enqueued — see class doc comment.
        var jobId = JobId.New();

        var document = new Document
        {
            Id = Guid.NewGuid(),
            Title = request.Title,
            Description = request.Description,
            Type = DocumentTypes.Generated,
            Status = DocumentStatuses.Pending,
            Source = DocumentSources.Template,
            CreatedBy = userId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        await store.AddAsync(document, ct);

        return new GenerateDocumentResult(document.Id, jobId);
    }
}
