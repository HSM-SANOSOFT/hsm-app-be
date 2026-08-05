using Hsm.Application.Abstractions;
using Hsm.Domain.Identity;

namespace Hsm.Application.Auth.Commands.Register;

/// <summary>
/// Public self-registration. The account is ALWAYS a Patient — client-supplied
/// roles are discarded by the edge and the request type has no field to carry
/// one — and created onboarding-complete (patients never do the staff
/// first-login flow).
///
/// <para>It returns the CREATED USER, not a token pair, for the same reason
/// <see cref="Login.LoginCommand"/> does: registering is application work and
/// the session that follows it is transport, which each door writes for
/// itself. The consequence is worth stating plainly — after this command no
/// human is ever handed a refresh token, so the only redeemable refresh tokens
/// left in the system belong to integration accounts.</para>
///
/// Anonymous by definition: the person signing up has no principal yet.
/// </summary>
[AllowAnonymousRequest]
public sealed record RegisterCommand(
    string Username,
    string Email,
    string Password,
    string FirstName,
    string FirstLastName,
    string? SecondName,
    string? SecondLastName,
    string? PhoneNumber,
    string? Gender) : ICommand<HsmUser>;
