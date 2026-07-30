using System.Text.Json.Nodes;
using Hsm.Application.Abstractions;
using Hsm.Domain.Templates;

namespace Hsm.Application.Templates.Queries.ValidateTemplate;

/// <summary>The validate result (frozen ValidateTemplateResponseDto).</summary>
public sealed record ValidateTemplateResult(
    bool Valid, Guid? TemplateId, IReadOnlyList<TemplateSchemaIssue>? Issues);

/// <summary>
/// Frozen validate: schema check then a compile probe — always a SUCCESS
/// response carrying valid/issues, never an error status (except unknown
/// identifier, which is 404). Writes no parse log. A query: nothing is persisted.
/// </summary>
public sealed record ValidateTemplateQuery(string Identifier, JsonNode? Data) : IQuery<ValidateTemplateResult>;
