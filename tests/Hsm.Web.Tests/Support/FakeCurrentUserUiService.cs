using Hsm.Contracts.Ui;

namespace Hsm.Web.Tests;

/// <summary>Contracts-level identity double: what the shell would see for a
/// signed-in user with the given roles, or for an anonymous visitor.</summary>
public sealed class FakeCurrentUserUiService(CurrentUserDto? user) : ICurrentUserUiService
{
    public static FakeCurrentUserUiService Anonymous => new(user: null);

    public static FakeCurrentUserUiService WithRoles(params string[] roles) =>
        new(new CurrentUserDto("00000000-0000-0000-0000-000000000001", "usuaria.prueba", roles));

    public Task<CurrentUserDto?> GetCurrentUserAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(user);
}
