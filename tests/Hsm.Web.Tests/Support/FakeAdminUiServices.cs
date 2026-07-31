using Hsm.Contracts.Ui;

namespace Hsm.Web.Tests;

/// <summary>Records sign-in attempts and answers with a configurable result.</summary>
public sealed class FakeSignInUiService(SignInResult result) : ISignInUiService
{
    public List<(string Username, string Password)> Attempts { get; } = [];

    public Task<SignInResult> SignInAsync(
        string username, string password, CancellationToken cancellationToken = default)
    {
        Attempts.Add((username, password));
        return Task.FromResult(result);
    }
}

/// <summary>Contracts-level double for the users screen.</summary>
public sealed class FakeUsersAdminUiService : IUsersAdminUiService
{
    public List<UserRowDto> Users { get; init; } = [];
    public IReadOnlyList<string> Roles { get; init; } = ["admin", "doctor", "nurse"];
    public List<NewStaffUserDto> CreateCalls { get; } = [];
    public List<(string UserId, string Role)> ChangeRoleCalls { get; } = [];

    public Task<UserListPageDto> ListUsersAsync(
        int page, int pageSize, CancellationToken cancellationToken = default) =>
        Task.FromResult(new UserListPageDto(Users, page, pageSize, Users.Count));

    public Task<UserRowDto> CreateStaffAsync(
        NewStaffUserDto command, CancellationToken cancellationToken = default)
    {
        CreateCalls.Add(command);
        var created = new UserRowDto(
            Guid.NewGuid().ToString(), command.Username, command.Email,
            $"{command.FirstName} {command.FirstLastName}", [command.Role],
            IsActive: true, OnboardingPending: true);
        Users.Add(created);
        return Task.FromResult(created);
    }

    public Task<UserRowDto> ChangeRoleAsync(
        string userId, string role, CancellationToken cancellationToken = default)
    {
        ChangeRoleCalls.Add((userId, role));
        var user = Users.Single(u => u.Id == userId);
        return Task.FromResult(user with { Roles = [role] });
    }

    public Task<IReadOnlyList<string>> GetAssignableRolesAsync(
        CancellationToken cancellationToken = default) => Task.FromResult(Roles);
}

/// <summary>
/// Contracts-level double for the integration accounts screen. Tokens are
/// handed out exactly once per issuing call, mirroring the real service.
/// </summary>
public sealed class FakeIntegrationAccountsUiService : IIntegrationAccountsUiService
{
    public List<IntegrationAccountDto> Accounts { get; init; } = [];
    public string NextAccessToken { get; set; } = "FAKE_ACCESS_TOKEN";
    public string NextRefreshToken { get; set; } = "FAKE_REFRESH_TOKEN";
    public int ProvisionCalls { get; private set; }
    public int IssueCalls { get; private set; }
    public List<string> RevokeCalls { get; } = [];

    public Task<IReadOnlyList<IntegrationAccountDto>> ListAccountsAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<IntegrationAccountDto>>([.. Accounts]);

    public Task<IssuedIntegrationTokensDto> ProvisionAsync(
        NewIntegrationAccountDto command, CancellationToken cancellationToken = default)
    {
        ProvisionCalls++;
        var account = new IntegrationAccountDto(
            Guid.NewGuid().ToString(), command.Name, command.Description,
            command.Functionality, IsActive: true, HasActiveToken: true);
        Accounts.Add(account);
        return Task.FromResult(new IssuedIntegrationTokensDto(
            account.Id, account.Name, NextAccessToken, NextRefreshToken));
    }

    public Task<IssuedIntegrationTokensDto> IssueTokensAsync(
        string accountId, CancellationToken cancellationToken = default)
    {
        IssueCalls++;
        var account = Accounts.Single(a => a.Id == accountId);
        return Task.FromResult(new IssuedIntegrationTokensDto(
            account.Id, account.Name, NextAccessToken, NextRefreshToken));
    }

    public Task RevokeTokensAsync(string accountId, CancellationToken cancellationToken = default)
    {
        RevokeCalls.Add(accountId);
        return Task.CompletedTask;
    }
}

/// <summary>Contracts-level double for the settings screen.</summary>
public sealed class FakeSettingsAdminUiService : ISettingsAdminUiService
{
    public Dictionary<string, List<SettingItemDto>> ItemsByCategory { get; init; } =
        new(StringComparer.Ordinal);

    public Dictionary<string, List<SettingAuditEntryDto>> AuditByCategory { get; init; } =
        new(StringComparer.Ordinal);

    public List<(string Category, IReadOnlyList<SettingChangeDto> Changes)> UpdateCalls { get; } = [];

    public Task<SettingsCategoryDto> GetSettingsAsync(
        string category, CancellationToken cancellationToken = default) =>
        Task.FromResult(new SettingsCategoryDto(
            category, ItemsByCategory.GetValueOrDefault(category) ?? []));

    public Task<SettingsCategoryDto> UpdateSettingsAsync(
        string category, IReadOnlyList<SettingChangeDto> changes, CancellationToken cancellationToken = default)
    {
        UpdateCalls.Add((category, changes));
        return GetSettingsAsync(category, cancellationToken);
    }

    public Task<IReadOnlyList<SettingAuditEntryDto>> GetAuditTrailAsync(
        string category, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SettingAuditEntryDto>>(
            AuditByCategory.GetValueOrDefault(category) ?? []);
}

/// <summary>Contracts-level double for the documents screen.</summary>
public sealed class FakeDocumentsAdminUiService : IDocumentsAdminUiService
{
    public List<DocumentRowDto> Documents { get; init; } = [];
    public List<string> UploadedFileNames { get; } = [];
    public List<string> UrlCalls { get; } = [];
    public List<string> DeleteCalls { get; } = [];
    public string PresignedUrl { get; set; } = "http://storage.local/presigned";

    public Task<DocumentListPageDto> ListDocumentsAsync(
        int page, int pageSize, CancellationToken cancellationToken = default) =>
        Task.FromResult(new DocumentListPageDto([.. Documents], page, pageSize, Documents.Count));

    public Task<IReadOnlyList<string>> UploadAsync(
        IReadOnlyList<UploadFileDto> files, CancellationToken cancellationToken = default)
    {
        foreach (var file in files)
        {
            UploadedFileNames.Add(file.FileName);
            Documents.Add(new DocumentRowDto(
                Guid.NewGuid().ToString(), file.FileName, "UPLOADED", "UPLOADED", DateTimeOffset.UtcNow));
        }

        return Task.FromResult<IReadOnlyList<string>>(
            [.. files.Select(_ => Guid.NewGuid().ToString())]);
    }

    public Task<string> GetDownloadUrlAsync(
        string documentId, CancellationToken cancellationToken = default)
    {
        UrlCalls.Add(documentId);
        return Task.FromResult(PresignedUrl);
    }

    public Task DeleteAsync(string documentId, CancellationToken cancellationToken = default)
    {
        DeleteCalls.Add(documentId);
        Documents.RemoveAll(d => d.Id == documentId);
        return Task.CompletedTask;
    }
}
