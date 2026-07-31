using Bunit;
using Hsm.Contracts.Ui;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
// The page class shares its short name with the Hsm.Web.Users endpoint
// namespace, which wins plain name lookup from this namespace.
using UsersPage = Hsm.Web.Pages.Admin.Users;

namespace Hsm.Web.Tests;

/// <summary>
/// The users-and-roles screen (plan U18, screen 2): listing renders, and the
/// create/role-change flows drive the contracts-declared UI service.
/// </summary>
public sealed class UsersPageTests : MudTestContext
{
    private static UserRowDto User(string username, string role) => new(
        Guid.NewGuid().ToString(), username, $"{username}@hsm.test",
        $"{username} Apellido", [role], IsActive: true, OnboardingPending: false);

    [Fact]
    public void Renders_the_user_listing()
    {
        var fake = new FakeUsersAdminUiService
        {
            Users = [User("ana.admin", "admin"), User("dora.doctor", "doctor")],
        };
        Services.AddSingleton<IUsersAdminUiService>(fake);

        var cut = Render<UsersPage>();

        Assert.Contains("Usuarios y roles", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("ana.admin", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("dora.doctor", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_flow_invokes_the_service_with_the_form_values()
    {
        var fake = new FakeUsersAdminUiService();
        Services.AddSingleton<IUsersAdminUiService>(fake);

        var cut = Render<UsersPage>();
        cut.Find("button[data-testid='toggle-create-user']").Click();
        cut.Find("input[data-testid='new-username']").Change("nuevo.doctor");
        cut.Find("input[data-testid='new-email']").Change("nuevo.doctor@hsm.test");
        cut.Find("input[data-testid='new-first-name']").Change("Nueva");
        cut.Find("input[data-testid='new-first-last-name']").Change("Doctora");
        cut.Find("input[data-testid='new-temp-password']").Change("Temp-Passw0rd");
        var roleSelect = cut.FindComponents<MudSelect<string>>()
            .Single(select => select.Instance.UserAttributes.ContainsKey("data-testid")
                && Equals(select.Instance.UserAttributes["data-testid"], "new-role"));
        await cut.InvokeAsync(() => roleSelect.Instance.ValueChanged.InvokeAsync("doctor"));
        cut.Find("button[data-testid='create-user-submit']").Click();

        var created = Assert.Single(fake.CreateCalls);
        Assert.Equal("nuevo.doctor", created.Username);
        Assert.Equal("nuevo.doctor@hsm.test", created.Email);
        Assert.Equal("doctor", created.Role);
        Assert.Equal("Temp-Passw0rd", created.TempPassword);
        // The listing refreshed and shows the new user.
        Assert.Contains("nuevo.doctor", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Role_change_flow_invokes_the_service_for_the_row()
    {
        var target = User("dora.doctor", "doctor");
        var fake = new FakeUsersAdminUiService { Users = [target] };
        Services.AddSingleton<IUsersAdminUiService>(fake);

        var cut = Render<UsersPage>();
        var rowSelect = cut.FindComponents<MudSelect<string>>()
            .Single(select => select.Instance.UserAttributes.ContainsKey("data-testid")
                && Equals(select.Instance.UserAttributes["data-testid"], "role-select-dora.doctor"));
        await cut.InvokeAsync(() => rowSelect.Instance.ValueChanged.InvokeAsync("nurse"));
        cut.Find("button[data-testid='apply-role-dora.doctor']").Click();

        Assert.Equal([(target.Id, "nurse")], fake.ChangeRoleCalls);
    }

    [Fact]
    public void Apply_button_stays_disabled_without_a_pending_change()
    {
        var fake = new FakeUsersAdminUiService { Users = [User("dora.doctor", "doctor")] };
        Services.AddSingleton<IUsersAdminUiService>(fake);

        var cut = Render<UsersPage>();

        var apply = cut.Find("button[data-testid='apply-role-dora.doctor']");
        Assert.True(apply.HasAttribute("disabled"));
    }
}
