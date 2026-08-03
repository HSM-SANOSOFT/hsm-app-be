using Hsm.Application.Abstractions;

namespace Hsm.Application.Auth.Commands.GeneratePin;

/// <summary>
/// PIN generation (frozen generatePin): an authenticated, validated NO-OP —
/// the frozen service method is a stub that persists nothing, and behavioral
/// parity means preserving exactly that.
///
/// Sliced in Task 15 rather than Task 10 because of its POLICY, not its body.
/// The frozen route is one of the two that are NOT @AllowPending, and
/// <c>OnboardingContractTests.Pending_user_is_blocked_from_feature_routes_but_reaches_profile</c>
/// uses it as the canonical pending-user refusal. With the edge onboarding
/// gate deleted, a route that dispatches nothing has nothing to carry that
/// policy — so this request type exists to carry it. Authenticated, no role
/// requirement, and deliberately NOT <c>[AllowPendingOnboarding]</c>.
/// </summary>
public sealed record GeneratePinCommand(string Purpose, string Target) : ICommand<Unit>;
