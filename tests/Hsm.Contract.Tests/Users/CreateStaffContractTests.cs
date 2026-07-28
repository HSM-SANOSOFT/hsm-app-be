using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Hsm.Contract.Tests.Users;

/// <summary>
/// POST /v1/user/staff — admin-only staff provisioning, pinned from the
/// frozen users.service createStaffUser: pending onboarding, temp password
/// emailed (never returned), patient-facing roles rejected, and the whole
/// creation inside one transaction.
/// </summary>
public sealed class CreateStaffContractTests(UsersApiFactory factory)
    : UsersContractTest(factory), IClassFixture<UsersApiFactory>
{
    private static object StaffPayload(string username, string role = "doctor", string? email = null) => new
    {
        username,
        email = email ?? $"{username}@contract.test",
        firstName = "Staff",
        firstLastName = "Member",
        role,
        tempPassword = "Temp-Passw0rd",
    };

    [Fact]
    public async Task Staff_account_is_created_pending_onboarding_with_the_requested_role()
    {
        var (bearer, _) = await AdminBearerAsync();
        var username = Unique("newstaff");

        var response = await Api.PostJsonAsync(
            Client, "/v1/user/staff", StaffPayload(username, role: "nurse"), bearer: bearer);

        AssertSuccessEnvelope(response, 201, "/v1/user/staff");
        var data = response.Data;
        Assert.Equal(username, data.GetProperty("username").GetString());
        // Pending forced first-login onboarding.
        Assert.Equal(JsonValueKind.Null, data.GetProperty("onboardingCompletedAt").ValueKind);
        // The temp password is NEVER in the response.
        Assert.DoesNotContain("Temp-Passw0rd", response.RawBody, StringComparison.Ordinal);
        Assert.False(data.TryGetProperty("password", out _));
        Assert.False(data.TryGetProperty("tempPassword", out _));

        // Role rows carry the requested role with its resolved domain.
        var id = Guid.Parse(data.GetProperty("id").GetString()!);
        var roles = await Factory.WithDbAsync(db => db.UserRoles.Where(r => r.UserId == id).ToListAsync());
        var role = Assert.Single(roles);
        Assert.Equal("nurse", role.Role);
        Assert.Equal("Clinical", role.Domain);
    }

    [Fact]
    public async Task Created_staff_receives_the_temp_password_by_email_and_can_authenticate()
    {
        var (bearer, _) = await AdminBearerAsync();
        var username = Unique("newstaff");

        var response = await Api.PostJsonAsync(
            Client, "/v1/user/staff", StaffPayload(username), bearer: bearer);
        AssertSuccessEnvelope(response, 201, "/v1/user/staff");

        var email = Factory.StaffEmails.Sent.Single(m => m.Username == username);
        Assert.Equal($"{username}@contract.test", email.Email);
        Assert.Equal("Temp-Passw0rd", email.TempPassword);

        // The staff member can sign in with the emailed credentials, still
        // flagged pending onboarding in the token claims.
        var login = await LoginAsync(username, email.TempPassword);
        AssertSuccessEnvelope(login, 201, "/v1/auth/login");
        var claims = Api.DecodeJwtPayload(login.AccessToken);
        Assert.Equal(JsonValueKind.Null, claims.GetProperty("onboardingCompletedAt").ValueKind);
    }

    [Fact]
    public async Task Patient_facing_roles_are_rejected()
    {
        var (bearer, _) = await AdminBearerAsync();

        foreach (var role in new[] { "patient", "family" })
        {
            var response = await Api.PostJsonAsync(
                Client, "/v1/user/staff", StaffPayload(Unique("nostaff"), role: role), bearer: bearer);

            var issue = AssertErrorEnvelope(response, 400, "COMMON.VALIDATION");
            Assert.Equal(
                "This endpoint provisions staff accounts only; patient/family roles are not allowed",
                issue.GetProperty("message").GetString());
        }
    }

    [Fact]
    public async Task Duplicate_username_rolls_back_leaving_no_partial_record()
    {
        var (bearer, _) = await AdminBearerAsync();
        var (existingUsername, _, _, _) = await SeedPatientAsync();
        var freshEmail = $"{Unique("fresh")}@contract.test";

        // Same username, different email: the user INSERT trips the unique
        // index. The frozen unique violation escaped as a bare 500 (it was no
        // HttpException); the envelope-wrapped 500 is the documented U12
        // divergence.
        var response = await Api.PostJsonAsync(
            Client, "/v1/user/staff",
            StaffPayload(existingUsername, email: freshEmail), bearer: bearer);
        AssertErrorEnvelope(response, 500, "COMMON.INTERNAL");

        // Transaction rolled back fully: no user row under the new email and
        // no role row left pointing at a user that never committed.
        var usersWithEmail = await Factory.WithDbAsync(db =>
            db.Users.CountAsync(u => u.Email == freshEmail));
        Assert.Equal(0, usersWithEmail);
        var orphanRoleRows = await Factory.WithDbAsync(db =>
            db.UserRoles.CountAsync(r => !db.Users.Any(u => u.Id == r.UserId)));
        Assert.Equal(0, orphanRoleRows);
    }

    [Fact]
    public async Task Email_delivery_failure_does_not_undo_the_committed_account()
    {
        var (bearer, _) = await AdminBearerAsync();
        var username = Unique("newstaff");
        Factory.StaffEmails.FailNext = true;

        var response = await Api.PostJsonAsync(
            Client, "/v1/user/staff", StaffPayload(username), bearer: bearer);

        // Frozen: the account is already committed; the enqueue failure is
        // logged and swallowed.
        AssertSuccessEnvelope(response, 201, "/v1/user/staff");
        var exists = await Factory.WithDbAsync(db => db.Users.AnyAsync(u => u.Username == username));
        Assert.True(exists);
    }

    [Fact]
    public async Task Invalid_role_and_short_temp_password_fail_validation()
    {
        var (bearer, _) = await AdminBearerAsync();

        var badRole = await Api.PostJsonAsync(
            Client, "/v1/user/staff", StaffPayload(Unique("x"), role: "warlock"), bearer: bearer);
        AssertValidationFailure(badRole, "role", "isIn");

        var shortPassword = await Api.PostJsonAsync(
            Client, "/v1/user/staff", new
            {
                username = Unique("x"),
                email = $"{Unique("x")}@contract.test",
                firstName = "Staff",
                firstLastName = "Member",
                role = "doctor",
                tempPassword = "short",
            }, bearer: bearer);
        AssertValidationFailure(shortPassword, "tempPassword", "minLength");
    }

    [Fact]
    public async Task Non_admin_is_403()
    {
        var (username, password, _, _) = await SeedPatientAsync();
        var login = await LoginAsync(username, password);

        var response = await Api.PostJsonAsync(
            Client, "/v1/user/staff", StaffPayload(Unique("x")), bearer: login.AccessToken);

        AssertErrorEnvelope(response, 403, "COMMON.FORBIDDEN");
    }
}
