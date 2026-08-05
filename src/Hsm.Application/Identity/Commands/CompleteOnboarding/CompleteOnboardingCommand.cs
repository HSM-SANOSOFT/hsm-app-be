using Hsm.Application.Abstractions;
using Hsm.Domain.Identity;

namespace Hsm.Application.Identity.Commands.CompleteOnboarding;

/// <summary>
/// First-login onboarding completion for a pending staff account: confirm the
/// email, set password + phone atomically, and clear the pending flag.
///
/// <para>It returns the UPDATED USER rather than a fresh token pair. Setting
/// the password rotates the account's security stamp, which is what actually
/// retires every session opened before onboarding — including the caller's, so
/// the door that owns a session is the one that must reissue it
/// (<c>RefreshSignInAsync</c>). A command cannot do that, and after this change
/// there is no user refresh token left to replay in the first place.</para>
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
    : ICommand<HsmUser>;
