using System.Net.Http.Headers;
using System.Text.Json;

namespace Hsm.Api.Tests;

/// <summary>
/// The one HTTP helper the shell suites still need: a bearer-decorated GET
/// against the sidecar REST host. It is down to a single caller
/// (<c>AdminScreensTests</c>'s machine call) now that Task 13 has retired the
/// frozen envelope those suites used to unwrap, and retires with the
/// integration routes in Task 14.
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
