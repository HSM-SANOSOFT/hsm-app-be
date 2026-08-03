using Hsm.Application.Abstractions;
using Hsm.Domain.Docs;

namespace Hsm.Application.Docs.Queries.ListDocuments;

/// <summary>
/// GET /v1/docs (frozen listDocuments): createdBy-scoped page. Authenticated
/// only — every /v1/docs route carries a bare @Roles() in the frozen
/// controller. The scoping id is not carried separately: it is already baked
/// into <see cref="Filter"/> by the caller (edge/UI service), exactly as the
/// pre-slicing handler received it.
/// </summary>
public sealed record ListDocumentsQuery(DocumentListFilter Filter) : IQuery<ListDocumentsResult>;

/// <summary>The frozen listDocuments page shape.</summary>
public sealed record ListDocumentsResult(IReadOnlyList<Document> Items, int Total);
