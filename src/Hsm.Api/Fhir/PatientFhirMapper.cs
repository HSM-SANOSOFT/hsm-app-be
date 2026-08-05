using System.Text.Json;
using System.Text.Json.Nodes;
using FluentValidation;
using FluentValidation.Results;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Utility;
using Hl7.Fhir.Validation;
using Hsm.Application.Clinical;

namespace Hsm.Api.Fhir;

/// <summary>
/// Inbound FHIR Patient validation + field mapping.
///
/// Validation posture: structure, element types, and unknown-property
/// rejection are enforced (422), while terminology/code bindings are NOT — a
/// bad gender code passes (KTD6). Firely's strict deserializer supplies the
/// structural check; its coded-value complaints are deliberately tolerated.
///
/// Persistence mapping: only active, gender, birthDate, name, telecom,
/// address, and identifier(system/value/use) rows survive; other valid R4
/// fields are accepted and dropped. The stored jsonb carries the inbound
/// JSON verbatim, so reads echo what was accepted.
/// </summary>
public static class PatientFhirMapper
{
    private static readonly JsonSerializerOptions FhirOptions =
        new JsonSerializerOptions().ForFhir(ModelInfo.ModelInspector);

    public static CreatePatientInput Parse(JsonNode? body, byte[] rawUtf8)
    {
        if (body is not JsonObject resource)
        {
            throw Unprocessable("body", "FHIR resource body must be a JSON object");
        }

        Validate(rawUtf8);

        var identifiers = new List<PatientIdentifierInput>();
        if (resource["identifier"] is JsonArray identifierArray)
        {
            foreach (var entry in identifierArray)
            {
                // Persist only identifiers carrying both system and value.
                if (entry is JsonObject identifier
                    && StringOf(identifier["system"]) is { Length: > 0 } system
                    && StringOf(identifier["value"]) is { Length: > 0 } value)
                {
                    identifiers.Add(new PatientIdentifierInput(system, value, StringOf(identifier["use"])));
                }
            }
        }

        // active defaults to true when absent.
        var active = !(resource["active"] is JsonValue activeValue
            && activeValue.GetValueKind() == JsonValueKind.False);

        return new CreatePatientInput(
            Active: active,
            Gender: StringOf(resource["gender"]),
            BirthDate: StringOf(resource["birthDate"]),
            NameJson: NonEmptyArrayJson(resource["name"]),
            TelecomJson: NonEmptyArrayJson(resource["telecom"]),
            AddressJson: NonEmptyArrayJson(resource["address"]),
            Identifiers: identifiers);
    }

    /// <summary>Entity → FHIR Patient JSON projection.</summary>
    public static JsonObject ToJson(Domain.Clinical.Patient patient)
    {
        var resource = new JsonObject
        {
            ["resourceType"] = "Patient",
            ["id"] = patient.Id.ToString(),
            ["active"] = patient.Active,
        };

        if (patient.Gender is not null)
        {
            resource["gender"] = patient.Gender;
        }

        if (patient.BirthDate is not null)
        {
            resource["birthDate"] = patient.BirthDate;
        }

        AddStoredArray(resource, "name", patient.NameJson);
        AddStoredArray(resource, "telecom", patient.TelecomJson);
        AddStoredArray(resource, "address", patient.AddressJson);

        if (patient.Identifiers.Count > 0)
        {
            var identifiers = new JsonArray();
            foreach (var row in patient.Identifiers)
            {
                var identifier = new JsonObject
                {
                    ["system"] = row.System,
                    ["value"] = row.Value,
                };
                if (row.Use is not null)
                {
                    identifier["use"] = row.Use;
                }

                identifiers.Add(identifier);
            }

            resource["identifier"] = identifiers;
        }

        return resource;
    }

    private static void Validate(byte[] rawUtf8)
    {
        Resource parsed;
        try
        {
            // The raw request bytes feed Firely directly — no
            // JsonNode→string→parse round trip.
            parsed = JsonSerializer.Deserialize<Resource>(rawUtf8, FhirOptions)!;
        }
        catch (DeserializationFailedException exception)
        {
            var structural = exception.Exceptions.FirstOrDefault(e => !IsTerminologyIssue(e));
            if (structural is not null)
            {
                throw Unprocessable("resource", structural.Message);
            }

            parsed = exception.PartialResult as Resource
                ?? throw Unprocessable("resource", exception.Message);
        }

        if (parsed is not Patient)
        {
            // Reject a mismatched resourceType instead of silently treating
            // it as a Patient.
            throw Unprocessable("resourceType", "Expected a Patient resource");
        }
    }

    /// <summary>
    /// Terminology/code-binding complaints pass; everything structural
    /// rejects.
    /// </summary>
    private static bool IsTerminologyIssue(CodedException exception) =>
        exception is CodedValidationException coded
        && coded.ErrorCode == CodedValidationException.INVALID_CODED_VALUE_CODE;

    /// <summary>
    /// Rendered as 422 by <see cref="FhirResponses"/> only — every other door
    /// treats a <see cref="ValidationException"/> as a 400.
    /// </summary>
    private static ValidationException Unprocessable(string field, string message) =>
        new([new ValidationFailure(field, message)]);

    private static string? StringOf(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.String
            ? value.GetValue<string>()
            : null;

    /// <summary>Arrays are stored only when non-empty.</summary>
    private static string? NonEmptyArrayJson(JsonNode? node) =>
        node is JsonArray { Count: > 0 } array ? array.ToJsonString() : null;

    private static void AddStoredArray(JsonObject resource, string property, string? storedJson)
    {
        if (storedJson is not null)
        {
            resource[property] = JsonNode.Parse(storedJson);
        }
    }
}
