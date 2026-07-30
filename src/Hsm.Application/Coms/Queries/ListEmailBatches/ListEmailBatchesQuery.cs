using Hsm.Application.Abstractions;
using Hsm.Domain.Coms;

namespace Hsm.Application.Coms.Queries.ListEmailBatches;

/// <summary>Frozen listBatches: createdAt DESC with optional filters. Authenticated only.</summary>
public sealed record ListEmailBatchesQuery(BatchListFilter Filter) : IQuery<IReadOnlyList<EmailBatch>>;
