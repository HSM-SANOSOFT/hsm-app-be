using Hsm.Application.Abstractions;

namespace Hsm.Application.Auth.Commands.GeneratePin;

/// <summary>
/// The frozen stub, verbatim: it logged nothing and persisted nothing. The
/// value of this handler is that a request type now exists to carry the
/// route's authentication and onboarding policy — see
/// <see cref="GeneratePinCommand"/>.
/// </summary>
public sealed class GeneratePinHandler : IRequestHandler<GeneratePinCommand, Unit>
{
    public Task<Unit> HandleAsync(GeneratePinCommand request, CancellationToken ct) =>
        Task.FromResult(Unit.Value);
}
