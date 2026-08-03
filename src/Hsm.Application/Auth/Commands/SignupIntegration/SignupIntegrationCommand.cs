using Hsm.Application.Abstractions;
using Hsm.Domain.Identity;

namespace Hsm.Application.Auth.Commands.SignupIntegration;

/// <summary>
/// Admin-driven integration account provisioning (frozen signupIntegration):
/// mints a machine account plus its first long-lived token pair. Admin-only —
/// this is how a permanent, non-expiring-in-practice credential enters the
/// system, so it is gated at the request, not just at the two edges that
/// dispatch it (the REST route and the admin screen).
/// </summary>
[RequireRole(Roles.Admin)]
public sealed record SignupIntegrationCommand(string Name, string Description, string Functionality)
    : ICommand<TokenPair>;
