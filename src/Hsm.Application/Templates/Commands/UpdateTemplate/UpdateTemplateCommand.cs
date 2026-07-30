using Hsm.Application.Abstractions;
using Hsm.Domain.Templates;

namespace Hsm.Application.Templates.Commands.UpdateTemplate;

/// <summary>
/// Frozen update: category immutable, per-field patches, child block upserted
/// only when supplied and matching the STORED category. Authenticated only
/// — see <see cref="Hsm.Application.Templates.Commands.CreateTemplate.CreateTemplateCommand"/>
/// for why this is not admin-gated.
/// </summary>
public sealed record UpdateTemplateCommand(Guid Id, TemplatePayload Payload) : ICommand<Template>;
