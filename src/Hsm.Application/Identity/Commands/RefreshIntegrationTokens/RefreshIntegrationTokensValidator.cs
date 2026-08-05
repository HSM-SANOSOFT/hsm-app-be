using FluentValidation;

namespace Hsm.Application.Identity.Commands.RefreshIntegrationTokens;

/// <summary>
/// One rule, and it exists to make a caller error report as one. A body of
/// <c>{}</c> binds <c>RawRefreshToken</c> as null; without this the handler
/// reaches <c>HashRefreshToken(null)</c>, and an <c>ArgumentNullException</c>
/// leaves the closed exception set through its default branch as a 500 — a
/// malformed request rendered as a server fault, on the one route in this module
/// that anonymous callers can reach.
///
/// <para>It does not check the token's SHAPE. Length or an alphabet check would
/// let a caller distinguish "not a token" from "not your token" by status code,
/// and the module's whole refusal policy is that those are the same answer. An
/// unparseable value hashes fine and matches no row.</para>
/// </summary>
public sealed class RefreshIntegrationTokensValidator
    : AbstractValidator<RefreshIntegrationTokensCommand>
{
    public RefreshIntegrationTokensValidator() =>
        RuleFor(x => x.RawRefreshToken).NotEmpty();
}
