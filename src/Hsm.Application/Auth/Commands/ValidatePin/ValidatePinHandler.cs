using Hsm.Application.Abstractions;

namespace Hsm.Application.Auth.Commands.ValidatePin;

/// <summary>The frozen stub, verbatim — see <see cref="ValidatePinCommand"/>.</summary>
public sealed class ValidatePinHandler : IRequestHandler<ValidatePinCommand, Unit>
{
    public Task<Unit> HandleAsync(ValidatePinCommand request, CancellationToken ct) =>
        Task.FromResult(Unit.Value);
}
