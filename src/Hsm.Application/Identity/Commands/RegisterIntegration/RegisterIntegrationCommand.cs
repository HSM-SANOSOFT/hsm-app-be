using Hsm.Application.Abstractions;
using Hsm.Domain.Identity;

namespace Hsm.Application.Identity.Commands.RegisterIntegration;

/// <summary>
/// Admin-driven integration account provisioning: mints a machine account plus
/// its first credential. Admin-only — this is how a long-lived, rotate-forever
/// credential enters the system, so it is gated at the request, not just at the
/// two edges that dispatch it (the REST route and the admin screen).
/// </summary>
[RequireRole(Roles.Admin)]
public sealed record RegisterIntegrationCommand(string Name, string Description, string Functionality)
    : ICommand<IntegrationTokens>;
