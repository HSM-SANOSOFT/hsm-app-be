using Hsm.Application.Abstractions;
using Hsm.Domain.Identity;

namespace Hsm.Application.Auth.Queries.GetMe;

/// <summary>
/// The calling user's own row. It carries NO id: the target is always the
/// actor, so one caller cannot read another's profile through this request no
/// matter what it sends.
///
/// <para><see cref="AllowPendingOnboardingAttribute"/> is load-bearing. A
/// pending staff account has to be able to see who it is — the shell decides
/// whether to show the onboarding screen from exactly this answer — and
/// without the exemption the pipeline would refuse the one read that tells a
/// client it is still pending.</para>
///
/// <para>It reads the USER ROW rather than the principal's claims. The claim
/// set is a cache written when the session opened; the row is what a role
/// change or a completed onboarding actually updates, and reading it is why
/// this replaced the frozen claim-shaped <c>GET /v1/auth/profile</c>.</para>
/// </summary>
[AllowPendingOnboarding]
public sealed record GetMeQuery : IQuery<HsmUser>;
