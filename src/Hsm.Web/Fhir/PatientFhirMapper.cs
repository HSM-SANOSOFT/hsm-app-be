using System.Text.Json;
using System.Text.Json.Nodes;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Utility;
using Hl7.Fhir.Validation;
using Hsm.Application.Clinical;
using Hsm.Application.Errors;

namespace Hsm.Web.Fhir;

/// <summary>
/// Inbound FHIR Patient validation + the frozen field mapping
/// (fhir-validation.pipe.ts + patient.translator.ts).
///
/// Validation posture mirrors the frozen Medplum validateResource: structure,
/// element types, and unknown-property rejection are enforced (422), while
/// terminology/code bindings are NOT — a bad gender code passes, exactly as it
/// did through Medplum (KTD6). Firely's strict deserializer supplies the
/// structural check; its coded-value complaints are deliberately tolerated.
///
/// Persistence mapping is the frozen translator's: only active, gender,
/// birthDate, name, telecom, address, and identifier(system/value/use) rows
/// survive; other valid R4 fields are accepted and dropped. The stored jsonb
/// carries the inbound JSON verbatim, so reads echo what was accepted.
/// </summary>
public static class PatientFhirMapper
{
    private static readonly JsonSerializerOptions FhirOptions =
        new JsonSerializerOptions().ForFhir(ModelInfo.ModelInspector);

    public static CreatePatientInput Parse(JsonNode? body)
    {
        if (body is not JsonObject resource)
        {
            throw Unprocessable("FHIR resource body must be a JSON object");
        }

        Validate(resource);

        var identifiers = new List<PatientIdentifierInput>();
        if (resource["identifier"] is JsonArray identifierArray)
        {
            foreach (var entry in identifierArray)
            {
                // The frozen translator persisted only identifiers carrying
                // both system and value (toIdentifierEntities filter).
                if (entry is JsonObject identifier
                    && StringOf(identifier["system"]) is { Length: > 0 } system
                    && StringOf(identifier["value"]) is { Length: > 0 } value)
                {
                    identifiers.Add(new PatientIdentifierInput(system, value, StringOf(identifier["use"])));
                }
            }
        }

        // Frozen fromFhir: active ?? true.
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

    /// <summary>The frozen toFhir projection: entity → FHIR Patient JSON.</summary>
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

    private static void Validate(JsonObject resource)
    {
        Resource parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<Resource>(resource.ToJsonString(), FhirOptions)!;
        }
        catch (DeserializationFailedException exception)
        {
            var structural = exception.Exceptions.FirstOrDefault(e => !IsTerminologyIssue(e));
            if (structural is not null)
            {
                throw Unprocessable(structural.Message);
            }

            parsed = exception.PartialResult as Resource
                ?? throw Unprocessable(exception.Message);
        }

        if (parsed is not Patient)
        {
            // The frozen pipe validated any resource type and the controller
            // then silently treated it as a Patient; rejecting the mismatch
            // is a deliberate, documented divergence.
            throw Unprocessable("Expected a Patient resource");
        }
    }

    /// <summary>
    /// Frozen Medplum parity: terminology/code-binding complaints pass;
    /// everything structural rejects.
    /// </summary>
    private static bool IsTerminologyIssue(CodedException exception) =>
        exception is CodedValidationException coded
        && coded.ErrorCode == CodedValidationException.INVALID_CODED_VALUE_CODE;

    private static ApiException Unprocessable(string message) =>
        new(StatusCodes.Status422UnprocessableEntity, message, errorLabel: "Unprocessable Entity");

    private static string? StringOf(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.String
            ? value.GetValue<string>()
            : null;

    /// <summary>The frozen translator stored arrays only when non-empty.</summary>
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
