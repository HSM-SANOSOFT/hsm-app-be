using Hsm.Application.Abstractions;
using Hsm.Domain.Docs;

namespace Hsm.Application.Docs.Queries.GetDocument;

/// <summary>
/// GET /v1/docs/{id}: owner-scoped, 404 otherwise.
/// Authenticated only. The owner id is not carried on the command — every
/// caller (the REST edge, the admin UI service) always scopes by the current
/// actor's own id, never an arbitrary one, so the handler reads it from
/// <see cref="ICurrentPrincipal"/> (the Task 8/Coms self-scoped precedent).
/// </summary>
public sealed record GetDocumentQuery(Guid Id) : IQuery<Document>;
