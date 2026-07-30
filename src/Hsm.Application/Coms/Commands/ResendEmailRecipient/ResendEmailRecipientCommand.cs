using Hsm.Application.Abstractions;

namespace Hsm.Application.Coms.Commands.ResendEmailRecipient;

/// <summary>
/// Frozen resendRecipient: re-enqueues exactly one recipient of its existing
/// batch — no new batch, no new recipient rows, no status reset here (the
/// dispatcher targets that recipient regardless of its current status).
/// Authenticated only.
/// </summary>
public sealed record ResendEmailRecipientCommand(Guid Id) : ICommand<string>;
