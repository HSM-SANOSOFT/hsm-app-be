using Hsm.Application.Abstractions;

namespace Hsm.Application.Templates.Commands.DeleteTemplate;

/// <summary>
/// Frozen delete: 404 when missing, and the in-use invariant surfaces as the
/// DOMAIN 409 (never a database foreign-key error): a template referenced as a
/// base by other templates cannot be deleted. Authenticated only — see
/// <see cref="Hsm.Application.Templates.Commands.CreateTemplate.CreateTemplateCommand"/>
/// for why this is not admin-gated.
/// </summary>
public sealed record DeleteTemplateCommand(Guid Id) : ICommand<Unit>;
