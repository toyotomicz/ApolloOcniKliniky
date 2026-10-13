using System.Net;
using System.Net.Http.Json;
using ApolloData.Api.Domain;
using ApolloData.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ApolloData.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class RegistrationTests(ApiFactory factory)
{
    [Fact]
    public async Task Plans_endpoint_returns_three_plans_with_standard_highlighted()
    {
        using var client = factory.CreateTestClient();
        var plans = await client.GetJsonAsync<List<PlanResponse>>("/api/plans");

        Assert.Equal(["basic", "standard", "premium"], plans.Select(p => p.Code));
        Assert.True(plans.Single(p => p.Code == "standard").Highlighted);
        Assert.Empty(plans.Single(p => p.Code == "basic").FieldGroups);
        Assert.Equal(["operationalContact", "serviceConfiguration"], plans.Single(p => p.Code == "premium").FieldGroups);
    }

    [Fact]
    public async Task Valid_registration_is_persisted_as_pending_with_utc_timestamp_and_sends_code()
    {
        using var client = factory.CreateTestClient();
        var email = Scenario.UniqueEmail();

        var response = await client.PostAsync("/api/registrations", Scenario.Registration(email, "premium"));

        await response.EnsureStatusAsync(HttpStatusCode.Created);
        var json = await response.ReadJsonAsync();
        Assert.True(json.GetProperty("verificationEmailSent").GetBoolean());

        var clinic = await factory.WithDbAsync(db => db.Clinics.SingleAsync(c => c.NormalizedEmail == email.ToLowerInvariant()));
        Assert.Equal(ClinicStatus.Pending, clinic.Status);
        Assert.Null(clinic.EmailVerifiedAt);
        Assert.Equal(DateTimeKind.Utc, clinic.CreatedAt.Kind);
        Assert.Equal("premium", clinic.PlanCode);
        Assert.Equal("301 00", clinic.PostalCode);
        Assert.Equal("+420777123456", clinic.Phone);
        Assert.Equal("Plzeň", clinic.City);
        Assert.NotNull(clinic.OperationalContact);
        Assert.Equal(4, clinic.ServiceConfiguration!.WorkplaceCount);
        Assert.Equal(OnboardingPreference.OnSite, clinic.ServiceConfiguration.OnboardingPreference);
        Assert.Matches(@"^\d{6}$", factory.Email.LastCodeFor(email));
    }

    [Fact]
    public async Task Missing_and_invalid_fields_return_field_errors()
    {
        using var client = factory.CreateTestClient();
        var response = await client.PostAsync("/api/registrations", new
        {
            planCode = "gold",
            name = "",
            organizationId = "12345678",
            email = "not-an-email",
            phone = "12",
            postalCode = "1234",
            privacyNoticeAccepted = false,
        });

        await response.EnsureStatusAsync(HttpStatusCode.BadRequest);
        var errors = (await response.ReadJsonAsync()).GetProperty("errors");
        foreach (var field in new[] { "planCode", "name", "organizationId", "email", "phone", "postalCode", "contactFirstName", "street", "city", "privacyNoticeAccepted" })
        {
            Assert.True(errors.TryGetProperty(field, out _), $"Expected an error for '{field}'.");
        }
    }

    [Fact]
    public async Task Standard_plan_requires_operational_contact()
    {
        using var client = factory.CreateTestClient();
        var body = Scenario.Registration(Scenario.UniqueEmail(), "standard");
        body.Remove("operationalContact");

        var response = await client.PostAsync("/api/registrations", body);

        await response.EnsureStatusAsync(HttpStatusCode.BadRequest);
        var errors = (await response.ReadJsonAsync()).GetProperty("errors");
        Assert.True(errors.TryGetProperty("operationalContact.name", out _));
        Assert.True(errors.TryGetProperty("operationalContact.email", out _));
        Assert.False(errors.TryGetProperty("serviceConfiguration.workplaceCount", out _));
    }

    [Fact]
    public async Task Premium_plan_requires_valid_service_configuration()
    {
        using var client = factory.CreateTestClient();
        var body = Scenario.Registration(Scenario.UniqueEmail(), "premium");
        body["serviceConfiguration"] = new { workplaceCount = 0 };

        var response = await client.PostAsync("/api/registrations", body);

        await response.EnsureStatusAsync(HttpStatusCode.BadRequest);
        var errors = (await response.ReadJsonAsync()).GetProperty("errors");
        Assert.True(errors.TryGetProperty("serviceConfiguration.workplaceCount", out _));
        Assert.True(errors.TryGetProperty("serviceConfiguration.onboardingPreference", out _));
    }

    [Fact]
    public async Task Basic_plan_ignores_fields_of_higher_plans()
    {
        using var client = factory.CreateTestClient();
        var email = Scenario.UniqueEmail();
        var body = Scenario.Registration(email, "basic");
        body["operationalContact"] = new { name = "Nemá se uložit", email = "x@example.test" };
        body["serviceConfiguration"] = new { workplaceCount = 3, onboardingPreference = "online" };

        var response = await client.PostAsync("/api/registrations", body);

        await response.EnsureStatusAsync(HttpStatusCode.Created);
        var clinic = await factory.WithDbAsync(db => db.Clinics.SingleAsync(c => c.NormalizedEmail == email));
        Assert.Null(clinic.OperationalContact);
        Assert.Null(clinic.ServiceConfiguration);
    }

    [Fact]
    public async Task Duplicate_email_is_rejected_case_insensitively()
    {
        using var client = factory.CreateTestClient();
        var email = await Scenario.RegisterAsync(factory, client);

        var response = await client.PostAsync("/api/registrations", Scenario.Registration(email.ToUpperInvariant()));

        await response.EnsureStatusAsync(HttpStatusCode.Conflict);
        Assert.Equal(1, await factory.WithDbAsync(db => db.Clinics.CountAsync(c => c.NormalizedEmail == email)));
    }

    [Fact]
    public async Task Over_posted_status_is_ignored_on_registration()
    {
        using var client = factory.CreateTestClient();
        var email = Scenario.UniqueEmail();
        var body = Scenario.Registration(email);
        body["status"] = "approved";
        body["emailVerifiedAt"] = DateTime.UtcNow;

        var response = await client.PostAsync("/api/registrations", body);

        await response.EnsureStatusAsync(HttpStatusCode.Created);
        var clinic = await factory.WithDbAsync(db => db.Clinics.SingleAsync(c => c.NormalizedEmail == email));
        Assert.Equal(ClinicStatus.Pending, clinic.Status);
        Assert.Null(clinic.EmailVerifiedAt);
    }

    [Fact]
    public async Task Registration_survives_smtp_failure_and_reports_it()
    {
        using var client = factory.CreateTestClient();
        var email = Scenario.UniqueEmail();
        factory.Email.Fail = true;
        try
        {
            var response = await client.PostAsync("/api/registrations", Scenario.Registration(email));
            await response.EnsureStatusAsync(HttpStatusCode.Created);
            Assert.False((await response.ReadJsonAsync()).GetProperty("verificationEmailSent").GetBoolean());

            var retry = await client.PostAsync("/api/clinic-auth/request-code", new { email });
            await retry.EnsureStatusAsync(HttpStatusCode.ServiceUnavailable);
        }
        finally
        {
            factory.Email.Fail = false;
        }

        // The failed attempts left no usable code, and a retry now succeeds immediately (no cooldown).
        var again = await client.PostAsync("/api/clinic-auth/request-code", new { email });
        await again.EnsureStatusAsync(HttpStatusCode.Accepted);
        Assert.Equal(1, factory.Email.CountFor(email));
        Assert.Equal(1, await factory.WithDbAsync(db => db.ClinicLoginChallenges.CountAsync(
            c => c.Clinic!.NormalizedEmail == email && c.DeliveredAt != null && c.InvalidatedAt == null)));
    }

    [Fact]
    public async Task Csrf_token_is_issued_over_plain_http_behind_a_proxy()
    {
        // Regression: with Secure cookies enabled, antiforgery used to throw on http://localhost (Nginx without TLS).
        using var http = factory.CreateClient(new() { BaseAddress = new Uri("http://localhost") });
        var response = await http.GetAsync("/api/security/csrf");
        await response.EnsureStatusAsync(HttpStatusCode.OK);
        Assert.False(string.IsNullOrEmpty((await response.ReadJsonAsync()).GetProperty("token").GetString()));
    }

    [Fact]
    public async Task Unsafe_request_without_csrf_token_is_rejected()
    {
        using var client = factory.CreateTestClient();
        var response = await client.Http.PostAsJsonAsync("/api/registrations", Scenario.Registration(Scenario.UniqueEmail()));
        await response.EnsureStatusAsync(HttpStatusCode.Forbidden);
    }

    private sealed record PlanResponse(string Code, string Name, bool Highlighted, List<string> FieldGroups);
}
