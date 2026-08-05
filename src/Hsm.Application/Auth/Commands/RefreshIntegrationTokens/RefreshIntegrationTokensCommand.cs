using Hsm.Application.Abstractions;

namespace Hsm.Application.Auth.Commands.RefreshIntegrationTokens;

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
/// </summary>
[AllowAnonymousRequest]
public sealed record RefreshIntegrationTokensCommand(string RawRefreshToken)
    : ICommand<IntegrationTokens>;
