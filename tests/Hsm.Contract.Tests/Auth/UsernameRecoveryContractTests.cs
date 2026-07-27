using System.Text.Json;

namespace Hsm.Contract.Tests.Auth;

/// <summary>
/// POST /v1/auth/username/recover — enumeration-safe (DoD C1): the observable
/// response is IDENTICAL whether or not an account owns the address.
/// </summary>
public sealed class UsernameRecoveryContractTests(AuthApiFactory factory)
    : AuthContractTest(factory), IClassFixture<AuthApiFactory>
{
    [Fact]
    public async Task Response_is_identical_with_and_without_an_account()
    {
        var (username, _, email, _) = await SeedPatientAsync();

        var known = await Api.PostJsonAsync(Client, "/v1/auth/username/recover", new { email });
        var unknown = await Api.PostJsonAsync(
            Client, "/v1/auth/username/recover", new { email = $"{Unique("ghost")}@contract.test" });

        Assert.Equal(known.Status, unknown.Status);
        AssertSuccessEnvelope(known, 201, "/v1/auth/username/recover");
        // Same data payload…
        Assert.Equal(known.Data.GetRawText(), unknown.Data.GetRawText());
        // …and same metadata shape (all fields but the clock).
        Assert.Equal(
            Canonical(known.Metadata),
            Canonical(unknown.Metadata));
        // No cookies on either.
        Assert.Empty(known.SetCookies);
        Assert.Empty(unknown.SetCookies);

        // The side channel that DOES differ is out-of-band: only the real
        // account received mail.
        Assert.Contains(Factory.Emailer.UsernameReminders, r => r.Email == email && r.Username == username);
    }

    [Fact]
    public async Task Inactive_accounts_are_treated_as_unknown()
    {
        var username = Unique("inactive");
        await Factory.SeedUserAsync(
            username, "Inactive-Passw0rd", "patient", DateTimeOffset.UtcNow, isActive: false);

        var response = await Api.PostJsonAsync(
            Client, "/v1/auth/username/recover", new { email = $"{username}@contract.test" });

        AssertSuccessEnvelope(response, 201, "/v1/auth/username/recover");
        Assert.DoesNotContain(Factory.Emailer.UsernameReminders, r => r.Email == $"{username}@contract.test");
    }

    private static string Canonical(JsonElement metadata)
    {
        var stable = new SortedDictionary<string, string?>(StringComparer.Ordinal);
        foreach (var property in metadata.EnumerateObject())
        {
            if (property.Name != "timestamp")
            {
                stable[property.Name] = property.Value.GetRawText();
            }
        }

        return string.Join("|", stable.Select(kv => $"{kv.Key}={kv.Value}"));
    }
}
