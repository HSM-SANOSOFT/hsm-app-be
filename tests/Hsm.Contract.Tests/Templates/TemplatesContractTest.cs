namespace Hsm.Contract.Tests.Templates;

/// <summary>
/// Base for the U14 template contract tests: the shared plumbing plus
/// template-creation helpers (templates routes accept ANY authenticated,
/// onboarded role).
/// </summary>
public abstract class TemplatesContractTest(TemplatesApiFactory factory)
    : ContractTest<TemplatesApiFactory>(factory)
{
    /// <summary>Seeds a doctor (any-role surface) and returns a bearer access token.</summary>
    protected async Task<string> BearerAsync(string role = "doctor") =>
        (await base.BearerAsync(role, onboarded: true)).Bearer;

    /// <summary>Creates a BASE template through the API and returns its id.</summary>
    protected async Task<string> CreateBaseAsync(string bearer, string? name = null)
    {
        var response = await Api.PostJsonAsync(Client, "/v1/templates", new
        {
            category = "BASE",
            name = name ?? Unique("base"),
            schema = new { },
            content = "<html><body>{{{body}}}</body></html>",
        }, bearer: bearer);
        Assert.True(response.Status == 201, $"base create failed: {response.RawBody}");
        return response.Data.GetProperty("template").GetProperty("id").GetString()!;
    }

    /// <summary>Creates an EMAIL_INTERNAL template through the API and returns its id.</summary>
    protected async Task<string> CreateEmailTemplateAsync(
        string bearer, string baseId, string? name = null, object? schema = null)
    {
        var response = await Api.PostJsonAsync(Client, "/v1/templates", new
        {
            category = "EMAIL_INTERNAL",
            name = name ?? Unique("email"),
            schema = schema ?? new { patientName = "string" },
            content = "<p>Hola {{patientName}}</p>",
            baseTemplateId = baseId,
            email = new
            {
                subject = "Hola {{patientName}}",
                fromEmail = "no-reply@hsm.test",
                fromName = "HSM",
            },
        }, bearer: bearer);
        Assert.True(response.Status == 201, $"email template create failed: {response.RawBody}");
        return response.Data.GetProperty("template").GetProperty("id").GetString()!;
    }

    /// <summary>Pins the frozen synthesized pagination for bare-array list payloads.</summary>
    protected static void AssertSyntheticPagination(ApiResponse response, int count) =>
        EnvelopeAssert.Pagination(response, page: 1, pageSize: count, totalItems: count, totalPages: 1);
}
