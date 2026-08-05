using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using Amazon.S3;
using Hsm.Application.Identity;
using Hsm.Application.Errors;
using Hsm.Contracts.Ui;
using Hsm.Web.Auth;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Hsm.Api.Tests.Shell;

/// <summary>
/// The U18 administrative screens through the real booted host (DoD C7):
/// direct-URL authorization posture per screen (anonymous → 302 to sign-in,
/// authenticated non-admin → 403, admin → 200), the sign-in screen's form
/// post establishing the same cookie session REST login issues, and each
/// screen's UI-service data path working end to end against real
/// PostgreSQL/RustFS.
/// </summary>
public sealed class AdminScreensFactory : ShellFactory
{
    public const string Bucket = "hsm-admin-screens-tests";

    protected override string DatabaseName => "hsm_admin_screens_test";

    protected override void ConfigureModule(IWebHostBuilder builder)
    {
        builder.UseSetting(
            "Storage:S3:Endpoint",
            Environment.GetEnvironmentVariable("Storage__S3__Endpoint") ?? "http://rustfs:9000");
        builder.UseSetting(
            "Storage:S3:AccessKey",
            Environment.GetEnvironmentVariable("Storage__S3__AccessKey") ?? "rustfs_user");
        builder.UseSetting(
            "Storage:S3:SecretKey",
            Environment.GetEnvironmentVariable("Storage__S3__SecretKey") ?? "rustfs_password");
        builder.UseSetting("Storage:S3:ForcePathStyle", "true");
        builder.UseSetting("Docs:Bucket", Bucket);
    }

    protected override async Task OnSchemaCreatedAsync()
    {
        var s3 = Services.GetRequiredService<IAmazonS3>();
        var buckets = await s3.ListBucketsAsync();
        if (buckets.Buckets is null || !buckets.Buckets.Any(b => b.BucketName == Bucket))
        {
            await s3.PutBucketAsync(Bucket);
        }
    }
}

public sealed class AdminScreensTests(AdminScreensFactory factory)
    : ShellTest<AdminScreensFactory>(factory), IClassFixture<AdminScreensFactory>
{
    public static readonly TheoryData<string, string> AdminScreens = new()
    {
        { "/usuarios", "Usuarios y roles" },
        { "/integraciones", "Cuentas de integración" },
        { "/configuracion", "Configuración" },
        { "/documentos", "Documentos" },
    };

    // ----- Direct-URL authorization posture (DoD: blocked by nav AND URL) --

    [Theory]
    [MemberData(nameof(AdminScreens))]
    public async Task Anonymous_direct_url_is_redirected_to_sign_in(string path, string title)
    {
        _ = title;
        using var response = await Client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var location = response.Headers.Location ?? throw new InvalidOperationException("no Location");
        Assert.Equal("/login", location.AbsolutePath);
    }

    [Theory]
    [MemberData(nameof(AdminScreens))]
    public async Task Authenticated_non_admin_direct_url_is_forbidden(string path, string title)
    {
        _ = title;
        var (session, _) = await SessionAsync(role: "doctor");

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Cookie", session);
        using var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(AdminScreens))]
    public async Task Admin_direct_url_reaches_the_screen(string path, string title)
    {
        var (session, _) = await SessionAsync(role: "admin");

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Cookie", session);
        using var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(title, body, StringComparison.Ordinal);
    }

    // ----- Screen 1: sign-in ----------------------------------------------

    [Fact]
    public async Task Sign_in_form_post_establishes_the_cookie_session()
    {
        var username = Unique("shell_admin");
        await Factory.SeedUserAsync(username, SeedPassword, "admin", DateTimeOffset.UtcNow);

        var (fields, cookies) = await LoadSignInFormAsync();
        fields["Input.Username"] = username;
        fields["Input.Password"] = SeedPassword;

        using var response = await PostSignInFormAsync(fields, cookies);

        // Static SSR turns the post-sign-in NavigateTo into a redirect, and
        // the response carries THE session cookie REST login issues — one
        // cookie now, not the frozen access/refresh pair, because a sliding
        // encrypted session has no browser refresh token to rotate.
        Assert.True(
            (int)response.StatusCode is >= 300 and < 400,
            $"expected redirect, got {(int)response.StatusCode}");
        var session = SessionCookieOf(response);
        Assert.DoesNotContain(
            response.Headers.GetValues("Set-Cookie"),
            c => c.StartsWith("refresh_token=", StringComparison.Ordinal));

        // And that cookie session reaches the shell.
        using var shellRequest = new HttpRequestMessage(HttpMethod.Get, "/");
        shellRequest.Headers.Add("Cookie", session);
        using var shellResponse = await Client.SendAsync(shellRequest);
        Assert.Equal(HttpStatusCode.OK, shellResponse.StatusCode);

        // And it reaches the OTHER door too, decrypted off the shared key
        // ring: the shell issued it, the API sidecar recognises it.
        using var apiRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/users?pageSize=1");
        apiRequest.Headers.Add("Cookie", session);
        using var apiResponse = await Client.SendAsync(apiRequest);
        Assert.Equal(HttpStatusCode.OK, apiResponse.StatusCode);
    }

    [Fact]
    public async Task Sign_in_form_post_with_bad_credentials_renders_the_error()
    {
        var (fields, cookies) = await LoadSignInFormAsync();
        fields["Input.Username"] = Unique("nobody");
        fields["Input.Password"] = "Wrong-Passw0rd";

        using var response = await PostSignInFormAsync(fields, cookies);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        // Dynamic text is HTML-entity-encoded by the static renderer ("ñ"
        // becomes &#xF1;), so the assertion pins the error surface plus the
        // ASCII tail of the message.
        Assert.Contains("data-testid=\"login-error\"", body, StringComparison.Ordinal);
        Assert.Contains("incorrectos.", body, StringComparison.Ordinal);
        // And no session cookie was issued.
        var setCookies = response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.ToList()
            : [];
        Assert.DoesNotContain(
            setCookies, c => c.StartsWith($"{SessionCookieName}=", StringComparison.Ordinal));
    }

    // ----- Screens 2-5 end to end through the UI services -----------------

    [Fact]
    public async Task Created_staff_user_can_sign_in_with_the_temporary_password()
    {
        var adminId = await SeedAdminAsync();
        var username = Unique("staff");
        const string tempPassword = "Temp-Passw0rd1";

        using var scope = CreateUiScope(adminId, "admin");
        var users = scope.ServiceProvider.GetRequiredService<IUsersAdminUiService>();
        var created = await users.CreateStaffAsync(new NewStaffUserDto(
            username, $"{username}@contract.test", "Nueva", null, "Doctora", null,
            null, "doctor", tempPassword));

        Assert.Equal(username, created.Username);
        Assert.True(created.OnboardingPending);
        Assert.Equal(["doctor"], created.Roles);

        // The provisioned account signs in through the REST login and gets a
        // session, even though its onboarding is still pending.
        var session = await SignInAsync(username, tempPassword);
        Assert.StartsWith($"{SessionCookieName}=", session, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Provisioned_integration_token_authenticates_a_machine_call()
    {
        var adminId = await SeedAdminAsync();

        using var scope = CreateUiScope(adminId, "admin");
        var integrations = scope.ServiceProvider.GetRequiredService<IIntegrationAccountsUiService>();
        var issued = await integrations.ProvisionAsync(
            new NewIntegrationAccountDto(Unique("machine"), "Cuenta de prueba", "dev"));

        Assert.False(string.IsNullOrEmpty(issued.AccessToken));

        // The machine call: the issued bearer token reaches the REST door and
        // authorizes there. An authenticated, non-role-gated read is the proof
        // now that GET /v1/auth/profile is gone — /api/v1/identity/me replaced
        // it and reads the USER row, which a machine account does not have.
        var reached = await Api.GetAsync(Client, "/api/v1/templates", bearer: issued.AccessToken);
        Assert.Equal(200, reached.Status);

        // And the role really was weighed, rather than the token merely being
        // accepted: the admin collection refuses this caller with a 403.
        var refused = await Api.GetAsync(Client, "/api/v1/users", bearer: issued.AccessToken);
        Assert.Equal(403, refused.Status);

        // The account shows in the listing with its active token; revoking
        // clears the active flag.
        var listed = await integrations.ListAccountsAsync();
        var account = listed.Single(a => a.Id == issued.AccountId);
        Assert.True(account.HasActiveToken);
        await integrations.RevokeTokensAsync(issued.AccountId);
        var afterRevoke = await integrations.ListAccountsAsync();
        Assert.False(afterRevoke.Single(a => a.Id == issued.AccountId).HasActiveToken);
    }

    [Fact]
    public async Task Settings_change_through_the_ui_service_writes_a_visible_audit_entry()
    {
        var adminId = await SeedAdminAsync();
        var newAddress = $"smtp.{Guid.NewGuid():N}.test";

        using var scope = CreateUiScope(adminId, "admin");
        var settings = scope.ServiceProvider.GetRequiredService<ISettingsAdminUiService>();
        var view = await settings.UpdateSettingsAsync(
            UiSettingCategories.Email, [new SettingChangeDto("SMTP_ADDRESS", newAddress)]);

        Assert.Equal(
            newAddress,
            view.Settings.Single(s => s.Key == "SMTP_ADDRESS").Value);

        var audit = await settings.GetAuditTrailAsync(UiSettingCategories.Email, page: 1, pageSize: 20);
        var entry = audit.Items.First(a => a.Key == "SMTP_ADDRESS");
        Assert.Equal(adminId.ToString(), entry.ChangedBy);
        Assert.Equal(newAddress, entry.NewValue);
    }

    [Fact]
    public async Task Documents_upload_retrieve_and_delete_through_the_ui_service()
    {
        var adminId = await SeedAdminAsync();
        const string content = "contenido del documento de prueba";

        using var scope = CreateUiScope(adminId, "admin");
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentsAdminUiService>();

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        var ids = await docs.UploadAsync(
            [new UploadFileDto("informe.txt", "text/plain", stream.Length, stream)]);
        var documentId = Assert.Single(ids);

        var page = await docs.ListDocumentsAsync(1, 20);
        Assert.Contains(page.Items, d => d.Id == documentId && d.Title == "informe.txt");

        // Retrieve: the presigned URL serves the uploaded bytes from RustFS.
        var url = await docs.GetDownloadUrlAsync(documentId);
        using var blobClient = new HttpClient();
        var fetched = await blobClient.GetStringAsync(new Uri(url));
        Assert.Equal(content, fetched);

        await docs.DeleteAsync(documentId);
        var afterDelete = await docs.ListDocumentsAsync(1, 20);
        Assert.DoesNotContain(afterDelete.Items, d => d.Id == documentId);
    }

    [Fact]
    public async Task Non_admin_session_is_blocked_by_the_pipeline()
    {
        var doctorId = await Factory.SeedUserAsync(
            Unique("gate_doctor"), SeedPassword, "doctor", DateTimeOffset.UtcNow);

        using var scope = CreateUiScope(doctorId, "doctor");
        var users = scope.ServiceProvider.GetRequiredService<IUsersAdminUiService>();
        var integrations = scope.ServiceProvider.GetRequiredService<IIntegrationAccountsUiService>();

        // Task 15 deleted UiServiceGate, whose UnauthorizedAccessException was
        // a second, UI-local authorization language. The refusal is now the
        // pipeline's — the SAME 403 ForbiddenException a REST caller gets, from
        // the request type's own [RequireRole(Roles.Admin)]. What is asserted is
        // still exactly what was asserted before: both calls are refused.
        await Assert.ThrowsAsync<ForbiddenException>(() => users.ListUsersAsync(1, 10));
        await Assert.ThrowsAsync<ForbiddenException>(() => integrations.ProvisionAsync(
            new NewIntegrationAccountDto("intruso", "no debería existir", "dev")));
    }

    // ----- plumbing -------------------------------------------------------

    private Task<Guid> SeedAdminAsync() => Factory.SeedUserAsync(
        Unique("screen_admin"), SeedPassword, "admin", DateTimeOffset.UtcNow);

    /// <summary>
    /// A service scope carrying the authenticated principal the way the shell
    /// does: the validated session lands in the scoped authentication state
    /// provider, and the UI services derive the pipeline's actor from it.
    /// </summary>
    private IServiceScope CreateUiScope(Guid userId, string role)
    {
        var scope = Factory.Services.CreateScope();
        var principal = new AuthPrincipal
        {
            Id = userId.ToString(),
            Username = $"scoped_{role}",
            Roles = [role],
            OnboardingCompletedAt = DateTimeOffset.UtcNow.ToString("O"),
            HasOnboardingClaim = true,
        };
        scope.ServiceProvider
            .GetRequiredService<HsmAuthenticationStateProvider>()
            .SetAuthenticationState(Task.FromResult(new AuthenticationState(
                principal.ToClaimsPrincipal())));
        return scope;
    }

    /// <summary>GETs the sign-in page and returns its hidden form fields
    /// (antiforgery token, handler name) plus the response cookies.</summary>
    private async Task<(Dictionary<string, string> Fields, List<(string Name, string Value)> Cookies)>
        LoadSignInFormAsync()
    {
        using var response = await Client.GetAsync(new Uri("/login", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(
            html, "<input[^>]*type=\"hidden\"[^>]*name=\"([^\"]+)\"[^>]*value=\"([^\"]*)\""))
        {
            fields[match.Groups[1].Value] = match.Groups[2].Value;
        }

        var cookies = new List<(string, string)>();
        if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            foreach (var cookie in setCookies)
            {
                var pair = cookie.Split(';')[0].Split('=', 2);
                cookies.Add((pair[0], pair[1]));
            }
        }

        return (fields, cookies);
    }

    private async Task<HttpResponseMessage> PostSignInFormAsync(
        Dictionary<string, string> fields, List<(string Name, string Value)> cookies)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/login")
        {
            Content = new FormUrlEncodedContent(fields),
        };
        if (cookies.Count > 0)
        {
            request.Headers.Add(
                "Cookie", string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}")));
        }

        return await Client.SendAsync(request);
    }
}
