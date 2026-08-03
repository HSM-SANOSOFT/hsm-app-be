using Hsm.Application.Abstractions;

namespace Hsm.Application.Auth.Commands.CompleteOnboarding;

/// <summary>
/// First-login onboarding completion for a pending staff account (frozen
/// completeOnboarding): confirm the email, set password + phone atomically,
/// clear the pending flag, and reissue tokens so the pre-onboarding refresh
/// token cannot be replayed.
///
/// <see cref="AllowPendingOnboardingAttribute"/> is load-bearing: without it a
/// pending user could never stop being pending — the only route out of the
/// pending state would itself require a completed state.
///
/// The command carries NO user id: the target is always the actor. A pending
/// user cannot complete somebody else's onboarding because the shape cannot
/// name one.
/// </summary>
[AllowPendingOnboarding]
public sealed record CompleteOnboardingCommand(string NewPassword, string PhoneNumber, string ConfirmEmail)
    : ICommand<TokenPair>;
