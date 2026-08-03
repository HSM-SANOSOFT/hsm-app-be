using Hsm.Application.Abstractions;

namespace Hsm.Application.Auth.Commands.Signup;

/// <summary>
/// Public self-registration. The account is ALWAYS a Patient — client-supplied
/// roles are discarded by the edge and the request type has no field to carry
/// one — and created onboarding-complete (patients never do the staff
/// first-login flow).
///
/// Anonymous by definition: the person signing up has no principal yet.
/// </summary>
[AllowAnonymousRequest]
public sealed record SignupCommand(
    string Username,
    string Email,
    string Password,
    string FirstName,
    string FirstLastName,
    string? SecondName,
    string? SecondLastName,
    string? PhoneNumber,
    string? Gender) : ICommand<TokenPair>;
