using Hsm.Application.Abstractions;
using Hsm.Domain.Coms;

namespace Hsm.Application.Coms.Queries.GetEmailBatch;

/// <summary>The batch with its recipient rows. Authenticated only.</summary>
public sealed record GetEmailBatchQuery(Guid Id) : IQuery<EmailBatch>;
