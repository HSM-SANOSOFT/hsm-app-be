using System.Text.Json.Nodes;
using Hsm.Application.Abstractions;

namespace Hsm.Application.Templates.Queries.DraftRender;

/// <summary>
/// Frozen draftRender: compose unsaved Handlebars source (optionally wrapped
/// in a BASE template) against sample data. Nothing is persisted or logged —
/// a query.
/// </summary>
public sealed record DraftRenderQuery(string Content, string? BaseTemplateId, JsonObject? SampleData)
    : IQuery<string>;
