using Hsm.Application.Abstractions;
using Hsm.Domain.Coms;

namespace Hsm.Application.Coms.Queries.GetEmailRecipient;

/// <summary>The recipient row. Authenticated only.</summary>
public sealed record GetEmailRecipientQuery(Guid Id) : IQuery<EmailRecipient>;
