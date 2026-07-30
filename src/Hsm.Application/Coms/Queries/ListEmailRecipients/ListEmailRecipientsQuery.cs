using Hsm.Application.Abstractions;
using Hsm.Domain.Coms;

namespace Hsm.Application.Coms.Queries.ListEmailRecipients;

/// <summary>Frozen listRecipients: id ASC with optional filters. Authenticated only.</summary>
public sealed record ListEmailRecipientsQuery(RecipientListFilter Filter) : IQuery<IReadOnlyList<EmailRecipient>>;
