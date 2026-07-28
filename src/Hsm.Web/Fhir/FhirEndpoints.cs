using Hsm.Application.Auth;
using Hsm.Application.Clinical;
using Hsm.Application.Errors;
using Hsm.Domain.Identity;
using Hsm.Web.Auth;

namespace Hsm.Web.Fhir;

/// <summary>
/// The FHIR R4 Patient facade at /fhir/R4/Patient (frozen
/// patient.controller.ts behind @FhirController): version-neutral path (no
/// /v1 prefix), raw FHIR responses, OperationOutcome errors, and the clinical
/// PHI roles gate (KTD11) — a single broad clinical-staff grant, with the
/// frozen RolesGuard's admin bypass and env-gated developer role.
///
/// The frozen Encounter and ServiceRequest facades are deliberately NOT here:
/// plan Scope Boundaries exclude clinical modules beyond patient lookup, and
/// the drop is recorded in the definition-of-done (C5) and pinned by the
/// route-closure contract test.
/// </summary>
public static class FhirEndpoints
{
    /// <summary>The frozen CLINICAL_STAFF_ROLES grant (fhir-controller.base.ts).</summary>
    public static readonly string[] ClinicalStaffRoles =
    [
        Roles.Doctor,
        Roles.Nurse,
        Roles.Technician,
        Roles.Therapist,
        Roles.Pharmacist,
    ];

    public static void MapFhirEndpoints(this IEndpointRouteBuilder app)
    {
        var patient = app.MapGroup("/fhir/R4/Patient");
        patient.MapGet("", (Delegate)SearchPatients);
        patient.MapGet("/{id}", (Delegate)ReadPatient);
        patient.MapPost("", (Delegate)CreatePatient);
    }

    /// <summary>Search by business identifier (?identifier=system|value) → searchset Bundle.</summary>
    private static Task SearchPatients(HttpContext ctx, SearchPatientsHandler handler) =>
        FhirResponses.ExecuteAsync(ctx, async () =>
        {
            await GateAsync(ctx);
            var (system, value) = ReadIdentifierToken(ctx);
            var patients = await handler.HandleAsync(system, value, ctx.RequestAborted);
            return FhirResponses.SearchsetBundle([.. patients.Select(PatientFhirMapper.ToJson)]);
        });

    /// <summary>Read by logical id → FHIR Patient (404 OperationOutcome when absent).</summary>
    private static Task ReadPatient(HttpContext ctx, string id, GetPatientHandler handler) =>
        FhirResponses.ExecuteAsync(ctx, async () =>
        {
            await GateAsync(ctx);
            var patient = await handler.HandleAsync(id, ctx.RequestAborted);
            return FhirResponses.Resource(PatientFhirMapper.ToJson(patient));
        });

    /// <summary>Create from a validated FHIR Patient body → stored resource (201).</summary>
    private static Task CreatePatient(HttpContext ctx, CreatePatientHandler handler) =>
        FhirResponses.ExecuteAsync(ctx, async () =>
        {
            await GateAsync(ctx);
            var body = await ReadJsonBodyAsync(ctx);
            var input = PatientFhirMapper.Parse(body);
            var patient = await handler.HandleAsync(input, ctx.RequestAborted);
            return FhirResponses.Resource(
                PatientFhirMapper.ToJson(patient), StatusCodes.Status201Created);
        });

    /// <summary>
    /// The frozen global guard chain on FHIR routes: authenticate, clinical
    /// roles (admin bypass), then the onboarding gate.
    /// </summary>
    private static async Task GateAsync(HttpContext ctx)
    {
        var principal = await RequestAuth.AuthenticateAsync(ctx, TokenKind.Access);
        RequestAuth.RequireRoles(ctx, principal, ClinicalStaffRoles);
        await RequestAuth.RequireOnboardingCompletedAsync(ctx, principal);
    }

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
                throw Unprocessable($"Search param '{key}' must be a single string value");
            }
        }

        var raw = ctx.Request.Query.TryGetValue("identifier", out var identifier)
            ? identifier.ToString()
            : null;
        if (string.IsNullOrEmpty(raw))
        {
            throw Unprocessable("Patient search requires an 'identifier' parameter");
        }

        // The frozen token grammar: ^(?:([^|]+)\|)?([^|]+)$ — at most one '|',
        // with non-empty parts on both sides.
        var parts = raw.Split('|');
        return parts switch
        {
            [{ Length: > 0 } value] => (null, value),
            [{ Length: > 0 } system, { Length: > 0 } value] => (system, value),
            _ => throw Unprocessable("Invalid identifier token for 'identifier' (expected 'system|value')"),
        };
    }

    private static async Task<System.Text.Json.Nodes.JsonNode?> ReadJsonBodyAsync(HttpContext ctx)
    {
        try
        {
            return await ctx.Request.ReadFromJsonAsync<System.Text.Json.Nodes.JsonNode>(ctx.RequestAborted);
        }
        catch (System.Text.Json.JsonException)
        {
            throw Unprocessable("FHIR resource body must be a JSON object");
        }
    }

    private static ApiException Unprocessable(string message) =>
        new(StatusCodes.Status422UnprocessableEntity, message, errorLabel: "Unprocessable Entity");
}
