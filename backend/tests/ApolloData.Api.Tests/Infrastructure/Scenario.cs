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
            ["publicPhones"] = new[] { "377 123 456" },
            ["publicEmails"] = new[] { "objednavky@example.test" },
            ["website"] = "www.ordinace-testovaci.cz",
            ["patientInfo"] = "Objednávky po–pá 7–15 h.",
            ["authorityConfirmed"] = true,
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

    /// <summary>Verifies the address with the emailed registration code and returns the registration token.</summary>
    public static async Task<string> VerifyEmailAsync(ApiFactory factory, TestClient client, string email)
    {
        await (await client.PostAsync("/api/registrations/email-code", new { email })).EnsureStatusAsync(HttpStatusCode.Accepted);
        var code = factory.Email.LastRegistrationCodeFor(email)!;
        var response = await client.PostAsync("/api/registrations/email-code/verify", new { email, code });
        await response.EnsureStatusAsync(HttpStatusCode.OK);
        return (await response.ReadJsonAsync()).GetProperty("verificationToken").GetString()!;
    }

    /// <summary>Verifies the e-mail of <paramref name="body"/> and submits the registration with the token.</summary>
    public static async Task<HttpResponseMessage> SubmitRegistrationAsync(ApiFactory factory, TestClient client, Dictionary<string, object?> body)
    {
        body["emailVerificationToken"] = await VerifyEmailAsync(factory, client, (string)body["email"]!);
        return await client.PostAsync("/api/registrations", body);
    }

    /// <summary>Registers a clinic; the client is signed in as that clinic afterwards.</summary>
    public static async Task<string> RegisterAsync(ApiFactory factory, TestClient client, string plan = "basic", string? email = null)
    {
        email ??= UniqueEmail();
        var response = await SubmitRegistrationAsync(factory, client, Registration(email, plan));
        await response.EnsureStatusAsync(HttpStatusCode.Created);
        return email;
    }

    /// <summary>Registers a clinic; registration signs the client in, so it can use the clinic portal right away.</summary>
    public static async Task<(string Email, Guid ClinicId)> RegisterAndSignInAsync(ApiFactory factory, TestClient client, string plan = "basic")
    {
        var email = UniqueEmail();
        var response = await SubmitRegistrationAsync(factory, client, Registration(email, plan));
        await response.EnsureStatusAsync(HttpStatusCode.Created);
        var json = await response.ReadJsonAsync();
        return (email, json.GetProperty("id").GetGuid());
    }

    /// <summary>Registers a clinic from a separate client and requests a login code for it on <paramref name="client"/>.</summary>
    public static async Task<string> RegisterAndRequestLoginCodeAsync(ApiFactory factory, TestClient client)
    {
        using var registrant = factory.CreateTestClient();
        var email = await RegisterAsync(factory, registrant);
        await (await client.PostAsync("/api/clinic-auth/request-code", new { email })).EnsureStatusAsync(HttpStatusCode.Accepted);
        return email;
    }

    public static async Task SignInAdminAsync(TestClient client)
    {
        var response = await client.PostAsync("/api/admin-auth/login",
            new { username = ApiFactory.AdminUsername, password = ApiFactory.AdminPassword });
        await response.EnsureStatusAsync(HttpStatusCode.OK);
    }
}
