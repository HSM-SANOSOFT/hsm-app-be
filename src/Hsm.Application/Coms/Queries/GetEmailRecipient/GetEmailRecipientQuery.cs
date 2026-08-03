using Hsm.Application.Abstractions;
using Hsm.Domain.Coms;

namespace Hsm.Application.Coms.Queries.GetEmailRecipient;

/// <summary>Frozen getRecipient. Authenticated only.</summary>
public sealed record GetEmailRecipientQuery(Guid Id) : IQuery<EmailRecipient>;
