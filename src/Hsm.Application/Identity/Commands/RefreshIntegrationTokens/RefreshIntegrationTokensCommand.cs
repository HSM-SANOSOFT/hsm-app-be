using Hsm.Application.Abstractions;

namespace Hsm.Application.Identity.Commands.RefreshIntegrationTokens;

/// <summary>
/// Redeems an integration account's opaque refresh token for a fresh
/// credential, retiring the presented one.
///
/// <see cref="AllowAnonymousRequestAttribute"/> because the REFRESH TOKEN is
/// the credential — the request is reachable precisely when the access token is
/// not, which is the whole point of refreshing. Nothing about the caller is
/// taken on trust: the command carries the raw token and nothing else, and the
/// handler learns which account it belongs to only by finding a stored digest
/// that matches. There is no claim here for an attacker to assert.
///
/// <para>A <c>[RequireRole]</c> could not run at all — there is no principal
/// for the pipeline to weigh — which is why the route's integrations-only
/// property comes from the token being the sole thing that identifies the
/// caller, rather than from a policy. A browser has no refresh token; a cookie
/// buys nothing here.</para>
///
/// <see cref="NoAmbientTransactionAttribute"/> for the same reason
/// <c>LoginCommand</c> carries it: a REFUSAL from this handler can have written
/// something that must survive it. Replaying a spent token revokes the account's
/// whole chain and then throws, and under the pipeline's transaction that
/// revocation would roll back with the exception — turning the alarm into a
/// no-op that also happens to log nothing. The handler opens its own transaction
/// around the rotation instead, so the happy path keeps its all-or-nothing
/// property.
/// </summary>
[AllowAnonymousRequest]
[NoAmbientTransaction]
public sealed record RefreshIntegrationTokensCommand(string RawRefreshToken)
    : ICommand<IntegrationTokens>;
