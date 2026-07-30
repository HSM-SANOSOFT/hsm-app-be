using Hsm.Application.Abstractions;
using Hsm.Domain.Templates;

namespace Hsm.Application.Templates.Queries.ListTemplates;

/// <summary>Frozen findAll: every template (optionally category-filtered), name ASC, children loaded.</summary>
public sealed record ListTemplatesQuery(string? Category) : IQuery<IReadOnlyList<Template>>;
