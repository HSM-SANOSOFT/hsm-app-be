using Hsm.Application.Abstractions;

namespace Hsm.Application.Docs.Commands.GenerateDocument;

/// <summary>
/// POST /v1/docs/generate (frozen generateDocument): persists a PENDING
/// GENERATED/TEMPLATE document and reserves the render job's id — the
/// response carries both ids, and the caller (the endpoint) enqueues the
/// render AFTER this command's transaction has committed. See
/// <see cref="GenerateDocumentHandler"/>'s doc comment for why the enqueue
/// cannot happen inside this handler. Authenticated only; the creator is not
/// carried on the command — read from <see cref="ICurrentPrincipal"/> (the
/// Task 8/Coms self-scoped precedent), since every caller always creates the
/// document as themselves.
/// </summary>
public sealed record GenerateDocumentCommand(
    string TemplateIdentifier,
    string DataJson,
    string Title,
    string? Description,
    string? EntityId,
    string? EntityType) : ICommand<GenerateDocumentResult>;

/// <summary>The frozen generateDocument response: the created document's id and the reserved job id.</summary>
public sealed record GenerateDocumentResult(Guid DocumentId, string JobId);
