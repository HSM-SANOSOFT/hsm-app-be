using System.Net.Http.Headers;
using System.Text.Json;

namespace Hsm.Api.Tests;

/// <summary>
/// The one HTTP helper the shell suites still need: a bearer-decorated GET
/// against the sidecar REST host, with a single caller —
/// <c>AdminScreensTests</c>'s machine call, which provisions an integration
/// through the shell's own UI service and then proves the resulting token
/// reaches the OTHER door. That cross-door hop is the whole point of the
/// helper, and it is why it did not retire with the integration routes: those
/// routes are exercised over HTTP by <c>IntegrationTokenTests</c>, but only a
/// shell test can start in process and end up at the API.
/// </summary>
public static class Api
{
    public static async Task<ApiResponse> GetAsync(
        HttpClient client, string path, string? bearer = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (bearer is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        return await ApiResponse.FromAsync(await client.SendAsync(request, CancellationToken.None));
    }
}

/// <summary>A parsed response: status, raw body, and JSON when parseable.</summary>
public sealed class ApiResponse
{
    public required int Status { get; init; }
    public required string RawBody { get; init; }
    public JsonElement? Json { get; init; }

    public JsonElement Root => Json ?? throw new InvalidOperationException($"Body was not JSON: {RawBody}");

    public static async Task<ApiResponse> FromAsync(HttpResponseMessage response)
    {
        using (response)
        {
            var raw = await response.Content.ReadAsStringAsync(CancellationToken.None);
            JsonElement? json = null;
            try
            {
                json = JsonDocument.Parse(raw).RootElement.Clone();
            }
            catch (JsonException)
            {
                // Non-JSON bodies stay raw.
            }

            return new ApiResponse
            {
                Status = (int)response.StatusCode,
                RawBody = raw,
                Json = json,
            };
        }
    }
}
