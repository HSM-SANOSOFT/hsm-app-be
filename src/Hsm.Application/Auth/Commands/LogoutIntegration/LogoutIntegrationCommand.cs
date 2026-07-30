using Hsm.Application.Abstractions;

namespace Hsm.Application.Auth.Commands.LogoutIntegration;

/// <summary>
/// Integration sign-out by presented token (frozen logoutIntegration).
/// Authenticated with NO pending-onboarding exemption: unlike
/// <c>LogoutCommand</c> this is an administration call about someone else's
/// machine account, not a self-service escape hatch, so a pending caller has
/// no business reaching it.
/// </summary>
public sealed record LogoutIntegrationCommand(string Token) : ICommand<Unit>;
