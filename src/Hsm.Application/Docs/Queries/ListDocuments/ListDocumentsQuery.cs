using Hsm.Application.Abstractions;
using Hsm.Contracts;
using Hsm.Domain.Docs;

namespace Hsm.Application.Docs.Queries.ListDocuments;

/// <summary>
/// GET /v1/docs: createdBy-scoped page. Authenticated
/// only. The scoping id is not carried separately: it is already baked
/// into <see cref="Filter"/> by the caller (edge/UI service), exactly as the
/// pre-slicing handler received it.
/// </summary>
public sealed record ListDocumentsQuery(DocumentListFilter Filter, int Page = 1, int PageSize = 20)
    : IQuery<PagedResult<Document>>;
