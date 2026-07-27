using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Hsm.Contract.Tests.Auth;

/// <summary>HTTP + envelope helpers shared by the auth contract tests.</summary>
public static class Api
{
    public static async Task<ApiResponse> PostJsonAsync(
        HttpClient client,
        string path,
        object body,
        string? bearer = null,
        (string Name, string Value)[]? cookies = null,
        (string Name, string Value)[]? headers = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        Decorate(request, bearer, cookies, headers);
        return await ApiResponse.FromAsync(await client.SendAsync(request));
    }

    public static async Task<ApiResponse> GetAsync(
        HttpClient client,
        string path,
        string? bearer = null,
        (string Name, string Value)[]? cookies = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        Decorate(request, bearer, cookies, headers: null);
        return await ApiResponse.FromAsync(await client.SendAsync(request));
    }

    public static async Task<ApiResponse> PatchJsonAsync(
        HttpClient client,
        string path,
        object body,
        string? bearer = null,
        (string Name, string Value)[]? cookies = null,
        (string Name, string Value)[]? headers = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, path) { Content = JsonContent.Create(body) };
        Decorate(request, bearer, cookies, headers);
        return await ApiResponse.FromAsync(await client.SendAsync(request));
    }

    public static async Task<ApiResponse> PutJsonAsync(
        HttpClient client,
        string path,
        object body,
        string? bearer = null,
        (string Name, string Value)[]? cookies = null,
        (string Name, string Value)[]? headers = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, path) { Content = JsonContent.Create(body) };
        Decorate(request, bearer, cookies, headers);
        return await ApiResponse.FromAsync(await client.SendAsync(request));
    }

    private static void Decorate(
        HttpRequestMessage request,
        string? bearer,
        (string Name, string Value)[]? cookies,
        (string Name, string Value)[]? headers)
    {
        if (bearer is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        if (cookies is { Length: > 0 })
        {
            request.Headers.Add("Cookie", string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}")));
        }

        if (headers is not null)
        {
            foreach (var (name, value) in headers)
            {
                request.Headers.Add(name, value);
            }
        }
    }

    /// <summary>Decodes a JWT payload segment without verification.</summary>
    public static JsonElement DecodeJwtPayload(string jwt)
    {
        var segment = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
        segment += (segment.Length % 4) switch { 2 => "==", 3 => "=", _ => string.Empty };
        return JsonDocument.Parse(Convert.FromBase64String(segment)).RootElement.Clone();
    }
}

/// <summary>A parsed response: status, raw body, JSON (when parseable), cookies.</summary>
public sealed class ApiResponse
{
    public required int Status { get; init; }
    public required string RawBody { get; init; }
    public JsonElement? Json { get; init; }
    public required IReadOnlyList<string> SetCookies { get; init; }

    public JsonElement Root => Json ?? throw new InvalidOperationException($"Body was not JSON: {RawBody}");
    public JsonElement Metadata => Root.GetProperty("metadata");
    public JsonElement Data => Root.GetProperty("data");
    public JsonElement Issue => Root.GetProperty("issue");
    public bool HasData => Root.TryGetProperty("data", out _);

    public string AccessToken => Data.GetProperty("access_token").GetString()!;
    public string RefreshToken => Data.GetProperty("refresh_token").GetString()!;

    public string? SetCookieFor(string name) =>
        SetCookies.FirstOrDefault(c => c.StartsWith($"{name}=", StringComparison.Ordinal));

    public static async Task<ApiResponse> FromAsync(HttpResponseMessage response)
    {
        using (response)
        {
            var raw = await response.Content.ReadAsStringAsync();
            JsonElement? json = null;
            try
            {
                json = JsonDocument.Parse(raw).RootElement.Clone();
            }
            catch (JsonException)
            {
                // Non-JSON bodies (e.g. the raw CSRF rejection) stay raw.
            }

            var cookies = response.Headers.TryGetValues("Set-Cookie", out var values)
                ? values.ToList()
                : [];
            return new ApiResponse
            {
                Status = (int)response.StatusCode,
                RawBody = raw,
                Json = json,
                SetCookies = cookies,
            };
        }
    }
}
