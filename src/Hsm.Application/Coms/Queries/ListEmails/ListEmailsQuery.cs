using Hsm.Application.Abstractions;
using Hsm.Contracts;

namespace Hsm.Application.Coms.Queries.ListEmails;

/// <summary>createdAt DESC with optional filters. Authenticated only.</summary>
public sealed record ListEmailsQuery(EmailListFilter Filter, int Page = 1, int PageSize = 20)
    : IQuery<PagedResult<EmailBatchSummary>>;
