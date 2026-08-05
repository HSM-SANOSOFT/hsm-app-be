using System.Text.Json.Nodes;
using Hsm.Application.Abstractions;

namespace Hsm.Application.Coms.Commands.SendEmail;

/// <summary>
/// Resolve template (404), validate data against its schema BEFORE anything
/// is persisted or dispatched (400 'Template data validation failed'), then
/// batch + per-recipient rows in one transaction, then enqueue. Suppressed
/// addresses are recorded as SUPPRESSED and never dispatched. Authenticated
/// only — every <c>/v1/coms</c> route allows any onboarded role except the
/// provider webhook. The caller's id is not carried on the command; the
/// handler reads it from <see cref="ICurrentPrincipal"/> (the
/// self-scoped-command precedent), since <c>AuthorizationBehavior</c>
/// guarantees a non-null actor for a non-anonymous request.
/// </summary>
public sealed record SendEmailCommand(
    string? FromEmail,
    string? FromName,
    IReadOnlyList<string> ToEmails,
    string EmailTemplate,
    JsonObject Data,
    IReadOnlyList<string>? DocumentIds) : ICommand<SendEmailResult>;

/// <summary>The created batch's id and the reserved job id.</summary>
public sealed record SendEmailResult(Guid BatchId, string JobId);
