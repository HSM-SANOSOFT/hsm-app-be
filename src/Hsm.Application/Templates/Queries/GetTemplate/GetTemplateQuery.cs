using Hsm.Application.Abstractions;
using Hsm.Domain.Templates;

namespace Hsm.Application.Templates.Queries.GetTemplate;

/// <summary>Id-or-name lookup with children and base loaded.</summary>
public sealed record GetTemplateQuery(string Identifier) : IQuery<Template>;
