using System.Net;

namespace ApolloData.Api.Tests.Infrastructure;

/// <summary>Builders for common multi-step flows.</summary>
public static class Scenario
{
    public static string UniqueEmail(string prefix = "klinika") => $"{prefix}.{Guid.NewGuid():N}@example.test";

    public static Dictionary<string, object?> Registration(string email, string plan = "basic", string name = "Ordinace Testovací s.r.o.")
    {
        var body = new Dictionary<string, object?>
        {
            ["planCode"] = plan,
            ["name"] = name,
            ["organizationId"] = "25596641",
            ["contactFirstName"] = "Jana",
            ["contactLastName"] = "Nováková",
            ["email"] = email,
            ["phone"] = "+420 777 123 456",
            ["street"] = "Náměstí Republiky 1",
            ["city"] = "Plzeň",
            ["postalCode"] = "30100",
            ["privacyNoticeAccepted"] = true,
        };
        if (plan is "standard" or "premium")
        {
            body["operationalContact"] = new { name = "Petr Svoboda", email = "provoz@example.test", phone = "377 123 456" };
        }
        if (plan is "premium")
        {
            body["serviceConfiguration"] = new { workplaceCount = 4, onboardingPreference = "onSite", technicalContactEmail = "it@example.test", notes = "Testovací poznámka" };
        }
        return body;
    }

    public static async Task<string> RegisterAsync(ApiFactory factory, TestClient client, string plan = "basic", string? email = null)
    {
        email ??= UniqueEmail();
        var response = await client.PostAsync("/api/registrations", Registration(email, plan));
        await response.EnsureStatusAsync(HttpStatusCode.Created);
        return email;
    }

    /// <summary>Registers a clinic and signs the client in with the emailed one-time code.</summary>
    public static async Task<(string Email, Guid ClinicId)> RegisterAndSignInAsync(ApiFactory factory, TestClient client, string plan = "basic")
    {
        var email = await RegisterAsync(factory, client, plan);
        var code = factory.Email.LastCodeFor(email)!;
        var response = await client.PostAsync("/api/clinic-auth/verify-code", new { email, code });
        await response.EnsureStatusAsync(HttpStatusCode.OK);
        var json = await response.ReadJsonAsync();
        return (email, json.GetProperty("id").GetGuid());
    }

    public static async Task SignInAdminAsync(TestClient client)
    {
        var response = await client.PostAsync("/api/admin-auth/login",
            new { username = ApiFactory.AdminUsername, password = ApiFactory.AdminPassword });
        await response.EnsureStatusAsync(HttpStatusCode.OK);
    }
}
