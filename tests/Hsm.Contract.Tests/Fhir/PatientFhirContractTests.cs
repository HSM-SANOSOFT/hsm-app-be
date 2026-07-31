using System.Text.Json;

namespace Hsm.Contract.Tests.Fhir;

/// <summary>
/// The frozen /fhir/R4/Patient trio (patient.controller.ts): search by
/// business identifier, read by logical id, create from a validated FHIR
/// body. National-identifier lookup is this surface — the identifier token is
/// 'system|value' or bare 'value' exactly as the frozen FhirSearchPipe
/// normalized it, an unknown identifier is an EMPTY searchset Bundle, and a
/// missing logical id is a 404 OperationOutcome.
/// </summary>
public sealed class PatientFhirContractTests(FhirApiFactory factory)
    : FhirContractTest(factory), IClassFixture<FhirApiFactory>
{
    private const string CedulaSystem = "https://registrocivil.gob.ec/cedula";

    private static object PatientBody(string system, string value, string? use = "official") => new
    {
        resourceType = "Patient",
        gender = "female",
        birthDate = "1990-05-01",
        name = new[] { new { family = "Andrade", given = new[] { "María" } } },
        telecom = new[] { new { system = "phone", value = "+593999999999" } },
        address = new[] { new { city = "Quito", country = "EC" } },
        identifier = new[] { new { system, value, use } },
    };

    private async Task<(string Id, string Value)> CreateAsync(string bearer, string? value = null)
    {
        value ??= Unique("0912345678");
        var response = await Api.PostJsonAsync(
            Client, "/fhir/R4/Patient", PatientBody(CedulaSystem, value), bearer: bearer);
        var resource = AssertFhirResource(response, 201, "Patient");
        return (resource.GetProperty("id").GetString()!, value);
    }

    // ---- create -----------------------------------------------------------

    [Fact]
    public async Task Create_returns_the_stored_resource_raw_with_identifiers()
    {
        var bearer = await BearerAsync();
        var value = Unique("1712345678");

        var response = await Api.PostJsonAsync(
            Client, "/fhir/R4/Patient", PatientBody(CedulaSystem, value), bearer: bearer);

        var resource = AssertFhirResource(response, 201, "Patient");
        Assert.True(Guid.TryParse(resource.GetProperty("id").GetString(), out _));
        Assert.True(resource.GetProperty("active").GetBoolean());
        Assert.Equal("female", resource.GetProperty("gender").GetString());
        Assert.Equal("1990-05-01", resource.GetProperty("birthDate").GetString());
        Assert.Equal("Andrade", resource.GetProperty("name")[0].GetProperty("family").GetString());
        Assert.Equal("+593999999999", resource.GetProperty("telecom")[0].GetProperty("value").GetString());
        Assert.Equal("Quito", resource.GetProperty("address")[0].GetProperty("city").GetString());
        var identifier = Assert.Single(resource.GetProperty("identifier").EnumerateArray());
        Assert.Equal(CedulaSystem, identifier.GetProperty("system").GetString());
        Assert.Equal(value, identifier.GetProperty("value").GetString());
        Assert.Equal("official", identifier.GetProperty("use").GetString());
    }

    [Fact]
    public async Task Create_accepts_and_drops_valid_r4_fields_outside_the_frozen_mapping()
    {
        var bearer = await BearerAsync();

        // maritalStatus is valid R4: the frozen Medplum validation accepted it
        // and the frozen translator dropped it on persist.
        var response = await Api.PostJsonAsync(
            Client,
            "/fhir/R4/Patient",
            new { resourceType = "Patient", maritalStatus = new { text = "M" } },
            bearer: bearer);

        var resource = AssertFhirResource(response, 201, "Patient");
        Assert.True(resource.GetProperty("active").GetBoolean());
        Assert.False(resource.TryGetProperty("maritalStatus", out _));
    }

    [Fact]
    public async Task Create_drops_identifiers_missing_system_or_value()
    {
        var bearer = await BearerAsync();

        var response = await Api.PostJsonAsync(
            Client,
            "/fhir/R4/Patient",
            new { resourceType = "Patient", identifier = new[] { new { value = Unique("orphan") } } },
            bearer: bearer);

        // Frozen toIdentifierEntities filtered system-less identifiers; the
        // echo has no identifier[] at all.
        var resource = AssertFhirResource(response, 201, "Patient");
        Assert.False(resource.TryGetProperty("identifier", out _));
    }

    [Fact]
    public async Task Create_with_an_already_registered_identifier_is_409_duplicate()
    {
        var bearer = await BearerAsync();
        var (_, value) = await CreateAsync(bearer);

        var response = await Api.PostJsonAsync(
            Client, "/fhir/R4/Patient", PatientBody(CedulaSystem, value), bearer: bearer);

        var diagnostics = AssertOperationOutcome(response, 409, "duplicate");
        Assert.Equal("A patient with one of these identifiers already exists", diagnostics);
    }

    [Fact]
    public async Task Create_rejects_an_unknown_property_as_422_invalid()
    {
        var bearer = await BearerAsync();

        var response = await Api.PostJsonAsync(
            Client, "/fhir/R4/Patient", new { resourceType = "Patient", frobnicate = true }, bearer: bearer);

        var diagnostics = AssertOperationOutcome(response, 422, "invalid");
        Assert.Contains("frobnicate", diagnostics, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_rejects_a_wrong_primitive_type_as_422()
    {
        var bearer = await BearerAsync();

        var response = await Api.PostJsonAsync(
            Client, "/fhir/R4/Patient", new { resourceType = "Patient", active = "yes" }, bearer: bearer);

        AssertOperationOutcome(response, 422, "invalid");
    }

    [Fact]
    public async Task Create_rejects_a_malformed_birthDate_as_422()
    {
        var bearer = await BearerAsync();

        var response = await Api.PostJsonAsync(
            Client, "/fhir/R4/Patient", new { resourceType = "Patient", birthDate = "not-a-date" }, bearer: bearer);

        AssertOperationOutcome(response, 422, "invalid");
    }

    [Fact]
    public async Task Create_accepts_an_uncoded_gender_value()
    {
        // The frozen Medplum validation did NOT check terminology bindings
        // (KTD6): a bad code passed structural validation and round-tripped.
        var bearer = await BearerAsync();

        var response = await Api.PostJsonAsync(
            Client, "/fhir/R4/Patient", new { resourceType = "Patient", gender = "banana" }, bearer: bearer);

        var resource = AssertFhirResource(response, 201, "Patient");
        Assert.Equal("banana", resource.GetProperty("gender").GetString());
    }

    [Fact]
    public async Task Create_rejects_a_non_object_body()
    {
        var bearer = await BearerAsync();

        var response = await Api.PostJsonAsync(
            Client, "/fhir/R4/Patient", new[] { 1, 2 }, bearer: bearer);

        var diagnostics = AssertOperationOutcome(response, 422, "invalid");
        Assert.Equal("FHIR resource body must be a JSON object", diagnostics);
    }

    [Fact]
    public async Task Create_rejects_a_non_patient_resource()
    {
        var bearer = await BearerAsync();

        var response = await Api.PostJsonAsync(
            Client,
            "/fhir/R4/Patient",
            new { resourceType = "Observation", status = "final", code = new { text = "x" } },
            bearer: bearer);

        var diagnostics = AssertOperationOutcome(response, 422, "invalid");
        Assert.Equal("Expected a Patient resource", diagnostics);
    }

    // ---- search (national-identifier lookup) ------------------------------

    [Fact]
    public async Task Search_by_system_and_value_returns_a_searchset_bundle()
    {
        var bearer = await BearerAsync();
        var (id, value) = await CreateAsync(bearer);

        var response = await Api.GetAsync(
            Client,
            $"/fhir/R4/Patient?identifier={Uri.EscapeDataString($"{CedulaSystem}|{value}")}",
            bearer: bearer);

        var bundle = AssertFhirResource(response, 200, "Bundle");
        Assert.Equal("searchset", bundle.GetProperty("type").GetString());
        Assert.Equal(1, bundle.GetProperty("total").GetInt32());
        var entry = Assert.Single(bundle.GetProperty("entry").EnumerateArray());
        var resource = entry.GetProperty("resource");
        Assert.Equal(id, resource.GetProperty("id").GetString());
        Assert.Equal(
            value,
            resource.GetProperty("identifier")[0].GetProperty("value").GetString());
    }

    [Fact]
    public async Task Search_by_bare_value_matches_across_systems()
    {
        var bearer = await BearerAsync();
        var (id, value) = await CreateAsync(bearer);

        var response = await Api.GetAsync(
            Client, $"/fhir/R4/Patient?identifier={Uri.EscapeDataString(value)}", bearer: bearer);

        var bundle = AssertFhirResource(response, 200, "Bundle");
        Assert.Equal(1, bundle.GetProperty("total").GetInt32());
        Assert.Equal(
            id,
            bundle.GetProperty("entry")[0].GetProperty("resource").GetProperty("id").GetString());
    }

    [Fact]
    public async Task Search_for_an_unknown_identifier_returns_an_empty_bundle_not_404()
    {
        var bearer = await BearerAsync();

        var response = await Api.GetAsync(
            Client, $"/fhir/R4/Patient?identifier={Unique("nobody")}", bearer: bearer);

        var bundle = AssertFhirResource(response, 200, "Bundle");
        Assert.Equal(0, bundle.GetProperty("total").GetInt32());
        Assert.Empty(bundle.GetProperty("entry").EnumerateArray());
    }

    [Fact]
    public async Task Search_without_identifier_is_422()
    {
        var bearer = await BearerAsync();

        var response = await Api.GetAsync(Client, "/fhir/R4/Patient", bearer: bearer);

        var diagnostics = AssertOperationOutcome(response, 422, "invalid");
        Assert.Equal("Patient search requires an 'identifier' parameter", diagnostics);
    }

    [Fact]
    public async Task Search_with_a_repeated_param_is_422()
    {
        var bearer = await BearerAsync();

        var response = await Api.GetAsync(
            Client, "/fhir/R4/Patient?identifier=a&identifier=b", bearer: bearer);

        var diagnostics = AssertOperationOutcome(response, 422, "invalid");
        Assert.Equal("Search param 'identifier' must be a single string value", diagnostics);
    }

    [Fact]
    public async Task Search_with_a_malformed_token_is_422()
    {
        var bearer = await BearerAsync();

        var response = await Api.GetAsync(
            Client, $"/fhir/R4/Patient?identifier={Uri.EscapeDataString("a|b|c")}", bearer: bearer);

        var diagnostics = AssertOperationOutcome(response, 422, "invalid");
        Assert.Equal("Invalid identifier token for 'identifier' (expected 'system|value')", diagnostics);
    }

    // ---- read -------------------------------------------------------------

    [Fact]
    public async Task Read_by_logical_id_returns_the_resource()
    {
        var bearer = await BearerAsync();
        var (id, value) = await CreateAsync(bearer);

        var response = await Api.GetAsync(Client, $"/fhir/R4/Patient/{id}", bearer: bearer);

        var resource = AssertFhirResource(response, 200, "Patient");
        Assert.Equal(id, resource.GetProperty("id").GetString());
        Assert.Equal(
            value,
            resource.GetProperty("identifier")[0].GetProperty("value").GetString());
    }

    [Fact]
    public async Task Read_of_an_unknown_id_is_404_operation_outcome()
    {
        var bearer = await BearerAsync();
        var unknown = Guid.NewGuid();

        var response = await Api.GetAsync(Client, $"/fhir/R4/Patient/{unknown}", bearer: bearer);

        var diagnostics = AssertOperationOutcome(response, 404, "not-found");
        Assert.Equal($"Patient '{unknown}' not found", diagnostics);
    }

    [Fact]
    public async Task Read_of_a_non_uuid_id_is_404_operation_outcome()
    {
        // Deliberate divergence: the frozen implementation surfaced a raw
        // database uuid-cast error as a 500 for this input; not-found is the
        // correct observable for a resource that cannot exist.
        var bearer = await BearerAsync();

        var response = await Api.GetAsync(Client, "/fhir/R4/Patient/not-a-uuid", bearer: bearer);

        var diagnostics = AssertOperationOutcome(response, 404, "not-found");
        Assert.Equal("Patient 'not-a-uuid' not found", diagnostics);
    }

    // ---- gate (the frozen clinical PHI roles gate, KTD11) -----------------

    [Fact]
    public async Task Unauthenticated_requests_are_401_operation_outcome()
    {
        var response = await Api.GetAsync(Client, "/fhir/R4/Patient?identifier=x");

        var diagnostics = AssertOperationOutcome(response, 401, "forbidden");
        Assert.Equal("Unauthorized", diagnostics);
    }

    [Fact]
    public async Task Non_clinical_roles_are_403()
    {
        var bearer = await BearerAsync("patient");

        var response = await Api.GetAsync(Client, "/fhir/R4/Patient?identifier=x", bearer: bearer);

        var diagnostics = AssertOperationOutcome(response, 403, "forbidden");
        // Task 15: the clinical grant moved off the edge onto the Patient
        // request types, and AuthorizationBehavior's refusal carries no
        // message, so the OperationOutcome renders its generic diagnostics.
        // The status and the FHIR issue code, asserted above, are the contract.
        Assert.Equal("Error", diagnostics);
    }

    [Fact]
    public async Task Integration_only_bearer_is_403()
    {
        // The frozen RolesGuard granted FHIR routes to clinical staff (and
        // admin); an integration account holding only the 'integration' role
        // was rejected — machine consumers need a clinical grant.
        var bearer = await IntegrationBearerAsync();

        var response = await Api.GetAsync(Client, "/fhir/R4/Patient?identifier=x", bearer: bearer);

        var diagnostics = AssertOperationOutcome(response, 403, "forbidden");
        Assert.Equal("Error", diagnostics);
    }

    [Fact]
    public async Task Every_clinical_role_and_admin_pass_the_gate()
    {
        foreach (var role in new[] { "doctor", "nurse", "technician", "therapist", "pharmacist", "admin" })
        {
            var bearer = await BearerAsync(role);
            var response = await Api.GetAsync(
                Client, $"/fhir/R4/Patient?identifier={Unique("gate")}", bearer: bearer);
            Assert.True(200 == response.Status, $"role '{role}' failed: {response.RawBody}");
        }
    }

    [Fact]
    public async Task Pending_onboarding_clinical_user_is_403()
    {
        var bearer = await BearerAsync("doctor", onboarded: false);

        var response = await Api.GetAsync(Client, "/fhir/R4/Patient?identifier=x", bearer: bearer);

        var diagnostics = AssertOperationOutcome(response, 403, "forbidden");
        // Task 15: same message-less pipeline refusal as the role cases above.
        Assert.Equal("Error", diagnostics);
    }

    // ---- persistence ------------------------------------------------------

    [Fact]
    public async Task Identifier_rows_are_normalized_child_rows_not_jsonb()
    {
        var bearer = await BearerAsync();
        var (id, value) = await CreateAsync(bearer);

        var rows = await Factory.WithDbAsync(db =>
            Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
                db.PatientIdentifiers.Where(i => i.PatientId == Guid.Parse(id))));

        var row = Assert.Single(rows);
        Assert.Equal(CedulaSystem, row.System);
        Assert.Equal(value, row.Value);
        Assert.Equal("official", row.Use);
    }
}
