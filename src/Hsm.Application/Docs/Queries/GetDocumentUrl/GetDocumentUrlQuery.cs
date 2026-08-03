using Hsm.Application.Abstractions;

namespace Hsm.Application.Docs.Queries.GetDocumentUrl;

/// <summary>
/// GET /v1/docs/{id}/url (frozen getDocumentUrl): presigns the LATEST
/// version's object, inline disposition, frozen default expiry. Authenticated
/// only; owner id read from <see cref="ICurrentPrincipal"/> — same rationale
/// as <see cref="Hsm.Application.Docs.Queries.GetDocument.GetDocumentQuery"/>.
/// </summary>
public sealed record GetDocumentUrlQuery(Guid Id) : IQuery<string>;
