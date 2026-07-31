using Hsm.Application.Abstractions;

namespace Hsm.Application.Auth.Commands.ValidatePin;

/// <summary>
/// PIN validation (frozen validatePin): the same authenticated, validated
/// NO-OP as <see cref="GeneratePin.GeneratePinCommand"/>, with the same
/// policy — authenticated, no role requirement, NOT
/// <c>[AllowPendingOnboarding]</c>. See that command for why both frozen
/// stubs became request types in Task 15.
/// </summary>
public sealed record ValidatePinCommand(string Purpose, string Target, double Code) : ICommand<Unit>;
