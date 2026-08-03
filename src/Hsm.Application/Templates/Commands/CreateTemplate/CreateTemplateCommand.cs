using Hsm.Application.Abstractions;
using Hsm.Domain.Templates;

namespace Hsm.Application.Templates.Commands.CreateTemplate;

/// <summary>
/// Frozen create: category-shape assertion, schema well-formedness, Handlebars
/// compile check, BASE-reference validation, name uniqueness (409), then
/// parent + child row in one transaction. Authenticated only — the frozen
/// templates.controller decorates every route with a bare <c>@Roles()</c>
/// (no role list), so ANY authenticated, onboarded user may create a
/// template; there is no admin restriction to preserve.
/// </summary>
public sealed record CreateTemplateCommand(TemplatePayload Payload) : ICommand<Template>;
