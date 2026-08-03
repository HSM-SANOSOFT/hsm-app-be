using FluentValidation;
using Hsm.Api.Auth;
using Hsm.Application.Abstractions;
using Hsm.Application.Clinical.Commands.CreatePatient;
using Hsm.Application.Clinical.Queries.GetPatient;
using Hsm.Application.Clinical.Queries.SearchPatients;

namespace Hsm.Api.Fhir;

/// <summary>
/// The FHIR R4 Patient facade at /fhir/R4/Patient (frozen
/// patient.controller.ts behind @FhirController): version-neutral path (no
/// /v1 prefix), raw FHIR responses, OperationOutcome errors, and the clinical
/// PHI roles gate (KTD11) — a single broad clinical-staff grant.
///
/// That grant lives entirely on the three Patient request types now
/// (<c>[RequireRole(Doctor, Nurse, Technician, Therapist, Pharmacist, Admin)]</c>
/// — admin is an explicit member because the pipeline has no blanket bypass;
/// see <c>GetPatientQuery</c>'s XML doc). Task 14 carried the grant here as
/// well, belt-and-suspenders, purely so the frozen "Insufficient permissions"
/// diagnostics survived; Task 15 removed the edge copy, so these routes
/// authenticate, install the actor, and decide nothing.
///
/// The frozen Encounter and ServiceRequest facades are deliberately NOT here:
/// plan Scope Boundaries exclude clinical modules beyond patient lookup, and
/// the drop is recorded in the definition-of-done (C5) and pinned by the
/// route-closure contract test.
/// </summary>
public static class FhirEndpoints
{
    public static void MapFhirEndpoints(this IEndpointRouteBuilder app)
    {
        var patient = app.MapGroup("/fhir/R4/Patient");
        patient.MapGet("", (Delegate)SearchPatients);
        patient.MapGet("/{id}", (Delegate)ReadPatient);
        patient.MapPost("", (Delegate)CreatePatient);
    }

    /// <summary>Search by business identifier (?identifier=system|value) → searchset Bundle.</summary>
    private static Task SearchPatients(HttpContext ctx, IDispatcher dispatcher) =>
        FhirResponses.ExecuteAsync(ctx, async () =>
        {
            await RequestAuth.GateAsync(ctx);
            var (system, value) = ReadIdentifierToken(ctx);
            var patients = await dispatcher.Send(
                new SearchPatientsQuery(system, value), ctx.RequestAborted);
            return FhirResponses.SearchsetBundle([.. patients.Select(PatientFhirMapper.ToJson)]);
        });

    /// <summary>Read by logical id → FHIR Patient (404 OperationOutcome when absent).</summary>
    private static Task ReadPatient(HttpContext ctx, string id, IDispatcher dispatcher) =>
        FhirResponses.ExecuteAsync(ctx, async () =>
        {
            await RequestAuth.GateAsync(ctx);
            var patient = await dispatcher.Send(new GetPatientQuery(id), ctx.RequestAborted);
            return FhirResponses.Resource(PatientFhirMapper.ToJson(patient));
        });

    /// <summary>Create from a validated FHIR Patient body → stored resource (201).</summary>
    private static Task CreatePatient(HttpContext ctx, IDispatcher dispatcher) =>
        FhirResponses.ExecuteAsync(ctx, async () =>
        {
            await RequestAuth.GateAsync(ctx);
            var (body, rawUtf8) = await ReadJsonBodyAsync(ctx);
            var input = PatientFhirMapper.Parse(body, rawUtf8);
            var patient = await dispatcher.Send(new CreatePatientCommand(input), ctx.RequestAborted);
            return FhirResponses.Resource(
                PatientFhirMapper.ToJson(patient), StatusCodes.Status201Created);
        });

    /// <summary>
    /// The frozen FhirSearchPipe for the Patient config: every query param
    /// must be single-valued; 'identifier' is required and must parse as
    /// 'system|value' (or bare 'value').
    /// </summary>
    private static (string? System, string Value) ReadIdentifierToken(HttpContext ctx)
    {
        foreach (var (key, values) in ctx.Request.Query)
        {
            if (values.Count != 1)
            {
                throw Unprocessable(key, $"Search param '{key}' must be a single string value");
            }
        }

        var raw = ctx.Request.Query.TryGetValue("identifier", out var identifier)
            ? identifier.ToString()
            : null;
        if (string.IsNullOrEmpty(raw))
        {
            throw Unprocessable("identifier", "Patient search requires an 'identifier' parameter");
        }

        // The frozen token grammar: ^(?:([^|]+)\|)?([^|]+)$ — at most one '|',
        // with non-empty parts on both sides.
        var parts = raw.Split('|');
        return parts switch
        {
            [{ Length: > 0 } value] => (null, value),
            [{ Length: > 0 } system, { Length: > 0 } value] => (system, value),
            _ => throw Unprocessable(
                "identifier", "Invalid identifier token for 'identifier' (expected 'system|value')"),
        };
    }

    /// <summary>
    /// Reads the request body ONCE as UTF-8 bytes — both the JsonNode view
    /// (field mapping) and the Firely validation parse consume the same
    /// buffer, with no re-serialization round trip.
    /// </summary>
    private static async Task<(System.Text.Json.Nodes.JsonNode? Node, byte[] RawUtf8)> ReadJsonBodyAsync(
        HttpContext ctx)
    {
        if (!ctx.Request.HasJsonContentType())
        {
            // ReadFromJsonAsync's frozen posture, preserved: a non-JSON
            // content type escapes as the generic 500 OperationOutcome.
            throw new InvalidOperationException(
                $"Unable to read the request as JSON because the request content type '{ctx.Request.ContentType}' is not a known JSON content type.");
        }

        using var buffer = new MemoryStream();
        await ctx.Request.Body.CopyToAsync(buffer, ctx.RequestAborted);
        var rawUtf8 = buffer.ToArray();
        try
        {
            return (System.Text.Json.Nodes.JsonNode.Parse(rawUtf8), rawUtf8);
        }
        catch (System.Text.Json.JsonException)
        {
            throw Unprocessable("body", "FHIR resource body must be a JSON object");
        }
    }

    /// <summary>
    /// Rendered as 422 by <see cref="FhirResponses"/> only — every other door
    /// treats a <see cref="ValidationException"/> as a 400.
    /// </summary>
    private static ValidationException Unprocessable(string field, string message) =>
        new([new FluentValidation.Results.ValidationFailure(field, message)]);
}
