using Hsm.Application.Abstractions;

namespace Hsm.Application.Coms.Commands.ResendEmailBatch;

/// <summary>
/// Frozen resendBatch: re-enqueues the whole batch (the dispatcher re-targets
/// PENDING/FAILED rows — no recipient rows are duplicated) and resets the
/// batch to PENDING with the fresh job id. Authenticated only.
/// </summary>
public sealed record ResendEmailBatchCommand(Guid Id) : ICommand<string>;
