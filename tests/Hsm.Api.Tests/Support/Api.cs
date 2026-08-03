using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Hsm.Api.Tests;

/// <summary>
/// HTTP helpers for the three shell suites carried over from the retired
/// frozen-contract tests (used only by <c>Shell/*Tests.cs</c> and
/// <see cref="ShellTest{TFactory}"/>). Task 12 rewrites those suites onto the
/// Identity cookie and this file retires with them.
/// </summary>
public static class Api
{
    public static async Task<ApiResponse> PostJsonAsync(
        HttpClient client, string path, object body, string? bearer = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        Decorate(request, bearer);
        return await ApiResponse.FromAsync(await client.SendAsync(request, CancellationToken.None));
    }

    public static async Task<ApiResponse> GetAsync(
        HttpClient client, string path, string? bearer = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        Decorate(request, bearer);
        return await ApiResponse.FromAsync(await client.SendAsync(request, CancellationToken.None));
    }

    private static void Decorate(HttpRequestMessage request, string? bearer)
    {
        if (bearer is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }
    }
}

/// <summary>A parsed response: status, raw body, and JSON when parseable.</summary>
public sealed class ApiResponse
{
    public required int Status { get; init; }
    public required string RawBody { get; init; }
    public JsonElement? Json { get; init; }

    public JsonElement Root => Json ?? throw new InvalidOperationException($"Body was not JSON: {RawBody}");
    public JsonElement Data => Root.GetProperty("data");
    public string AccessToken => Data.GetProperty("access_token").GetString()!;

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
