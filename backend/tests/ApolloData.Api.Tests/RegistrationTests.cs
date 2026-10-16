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
    public async Task Plans_endpoint_returns_the_three_registration_levels()
    {
        using var client = factory.CreateTestClient();
        var plans = await client.GetJsonAsync<List<PlanResponse>>("/api/plans");

        Assert.Equal(["basic", "standard", "premium"], plans.Select(p => p.Code));
        Assert.Equal("Úroveň 2", plans.Single(p => p.Code == "standard").Name);
        Assert.Empty(plans.Single(p => p.Code == "basic").FieldGroups);
        Assert.Equal(["operationalContact", "serviceConfiguration"], plans.Single(p => p.Code == "premium").FieldGroups);
    }

    [Fact]
    public async Task Verified_registration_is_persisted_as_pending_signs_in_and_sends_confirmation_with_pdf()
    {
        using var client = factory.CreateTestClient();
        var email = Scenario.UniqueEmail();

        var response = await Scenario.SubmitRegistrationAsync(factory, client, Scenario.Registration(email, "premium"));

        await response.EnsureStatusAsync(HttpStatusCode.Created);
        var json = await response.ReadJsonAsync();
        Assert.Equal(email, json.GetProperty("email").GetString());

        var clinic = await factory.WithDbAsync(db => db.Clinics.SingleAsync(c => c.NormalizedEmail == email.ToLowerInvariant()));
        Assert.Equal(ClinicStatus.Pending, clinic.Status);
        Assert.NotNull(clinic.EmailVerifiedAt);
        Assert.NotNull(clinic.AuthorityConfirmedAt);
        Assert.Equal(["377123456"], clinic.PublicPhones);
        Assert.Equal(["objednavky@example.test"], clinic.PublicEmails);
        Assert.Equal("https://www.ordinace-testovaci.cz", clinic.Website);
        Assert.Equal("Objednávky po–pá 7–15 h.", clinic.PatientInfo);
        Assert.Equal(DateTimeKind.Utc, clinic.CreatedAt.Kind);
        Assert.Equal("premium", clinic.PlanCode);
        Assert.Equal("301 00", clinic.PostalCode);
        Assert.Equal("+420777123456", clinic.Phone);
        Assert.Equal("Plzeň", clinic.City);
        Assert.NotNull(clinic.OperationalContact);
        Assert.Equal(4, clinic.ServiceConfiguration!.WorkplaceCount);
        Assert.Equal(OnboardingPreference.OnSite, clinic.ServiceConfiguration.OnboardingPreference);

        // The e-mail was verified up front, so the clinic is signed in right away.
        await (await client.GetAsync("/api/clinic/me")).EnsureStatusAsync(HttpStatusCode.OK);
        Assert.True(await factory.WithDbAsync(db => db.RegistrationEmailVerifications.AnyAsync(
            v => v.NormalizedEmail == email && v.ConsumedAt != null)));

        var confirmation = await factory.Email.WaitForAsync(m => m.ToEmail == email && m.Subject.Contains("přijata"));
        var pdf = Assert.Single(confirmation.Attachments!);
        Assert.Equal("application/pdf", pdf.ContentType);
        Assert.Equal("%PDF"u8.ToArray(), pdf.Content[..4]);
    }

    [Fact]
    public async Task Registration_without_verified_email_is_rejected()
    {
        using var client = factory.CreateTestClient();
        var email = Scenario.UniqueEmail();

        var missing = await client.PostAsync("/api/registrations", Scenario.Registration(email));
        await missing.EnsureStatusAsync(HttpStatusCode.BadRequest);
        var problem = await missing.ReadJsonAsync();
        Assert.Equal("email_not_verified", problem.GetProperty("code").GetString());
        Assert.True(problem.GetProperty("errors").TryGetProperty("email", out _));

        // A token proves only the address it was issued for.
        var otherToken = await Scenario.VerifyEmailAsync(factory, client, Scenario.UniqueEmail());
        var body = Scenario.Registration(email);
        body["emailVerificationToken"] = otherToken;
        await (await client.PostAsync("/api/registrations", body)).EnsureStatusAsync(HttpStatusCode.BadRequest);
        Assert.False(await factory.WithDbAsync(db => db.Clinics.AnyAsync(c => c.NormalizedEmail == email)));
    }

    [Fact]
    public async Task Verification_token_expires()
    {
        using var client = factory.CreateTestClient();
        var email = Scenario.UniqueEmail();
        var body = Scenario.Registration(email);
        body["emailVerificationToken"] = await Scenario.VerifyEmailAsync(factory, client, email);

        factory.Time.Advance(TimeSpan.FromMinutes(121));

        await (await client.PostAsync("/api/registrations", body)).EnsureStatusAsync(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Email_code_is_not_sent_to_a_registered_address()
    {
        using var client = factory.CreateTestClient();
        var email = await Scenario.RegisterAsync(factory, client);

        var response = await client.PostAsync("/api/registrations/email-code", new { email = email.ToUpperInvariant() });

        await response.EnsureStatusAsync(HttpStatusCode.Conflict);
        Assert.Equal("email_taken", (await response.ReadJsonAsync()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Email_code_is_stored_as_hash_and_locked_after_max_wrong_attempts()
    {
        using var client = factory.CreateTestClient();
        var email = Scenario.UniqueEmail();
        await (await client.PostAsync("/api/registrations/email-code", new { email })).EnsureStatusAsync(HttpStatusCode.Accepted);
        var code = factory.Email.LastRegistrationCodeFor(email)!;

        var stored = await factory.WithDbAsync(db => db.RegistrationEmailVerifications.SingleAsync(v => v.NormalizedEmail == email));
        Assert.Equal(32, stored.CodeHash.Length);
        Assert.DoesNotContain(code, Convert.ToHexString(stored.CodeHash));

        var wrong = code == "000000" ? "111111" : "000000";
        for (var i = 0; i < 5; i++)
        {
            await (await client.PostAsync("/api/registrations/email-code/verify", new { email, code = wrong }))
                .EnsureStatusAsync(HttpStatusCode.BadRequest);
        }
        await (await client.PostAsync("/api/registrations/email-code/verify", new { email, code }))
            .EnsureStatusAsync(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Email_code_resend_is_throttled_and_a_new_code_supersedes_the_old_one()
    {
        using var client = factory.CreateTestClient();
        var email = Scenario.UniqueEmail();
        await (await client.PostAsync("/api/registrations/email-code", new { email })).EnsureStatusAsync(HttpStatusCode.Accepted);
        var first = factory.Email.LastRegistrationCodeFor(email);

        var tooSoon = await client.PostAsync("/api/registrations/email-code", new { email });
        await tooSoon.EnsureStatusAsync(HttpStatusCode.TooManyRequests);
        Assert.Equal("code_throttled", (await tooSoon.ReadJsonAsync()).GetProperty("code").GetString());

        factory.Time.Advance(TimeSpan.FromSeconds(61));
        await (await client.PostAsync("/api/registrations/email-code", new { email })).EnsureStatusAsync(HttpStatusCode.Accepted);
        var second = factory.Email.LastRegistrationCodeFor(email);

        if (first != second)
        {
            await (await client.PostAsync("/api/registrations/email-code/verify", new { email, code = first }))
                .EnsureStatusAsync(HttpStatusCode.BadRequest);
        }
        await (await client.PostAsync("/api/registrations/email-code/verify", new { email, code = second }))
            .EnsureStatusAsync(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Public_contacts_are_validated()
    {
        using var client = factory.CreateTestClient();
        var body = Scenario.Registration(Scenario.UniqueEmail());
        body["publicPhones"] = new[] { "", " " };
        body["publicEmails"] = Array.Empty<string>();
        body["website"] = "neplatny web";

        var none = await client.PostAsync("/api/registrations", body);
        await none.EnsureStatusAsync(HttpStatusCode.BadRequest);
        var errors = (await none.ReadJsonAsync()).GetProperty("errors");
        Assert.True(errors.TryGetProperty("publicPhones.0", out _));
        Assert.True(errors.TryGetProperty("website", out _));

        body["publicPhones"] = new[] { "1", "377 123 456", "377 123 457", "377 123 458" };
        body["publicEmails"] = new[] { "neni-email" };
        var invalid = await client.PostAsync("/api/registrations", body);
        var invalidErrors = (await invalid.ReadJsonAsync()).GetProperty("errors");
        Assert.True(invalidErrors.TryGetProperty("publicPhones", out _));
        Assert.True(invalidErrors.TryGetProperty("publicPhones.0", out _));
        Assert.True(invalidErrors.TryGetProperty("publicEmails.0", out _));
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
            authorityConfirmed = false,
        });

        await response.EnsureStatusAsync(HttpStatusCode.BadRequest);
        var errors = (await response.ReadJsonAsync()).GetProperty("errors");
        foreach (var field in new[] { "planCode", "name", "organizationId", "email", "phone", "postalCode", "contactFirstName", "street", "city", "privacyNoticeAccepted", "authorityConfirmed", "publicPhones.0" })
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

        var response = await Scenario.SubmitRegistrationAsync(factory, client, body);

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
        body["isDemo"] = true;

        var response = await Scenario.SubmitRegistrationAsync(factory, client, body);

        await response.EnsureStatusAsync(HttpStatusCode.Created);
        var clinic = await factory.WithDbAsync(db => db.Clinics.SingleAsync(c => c.NormalizedEmail == email));
        Assert.Equal(ClinicStatus.Pending, clinic.Status);
        Assert.False(clinic.IsDemo);
    }

    [Fact]
    public async Task Email_code_delivery_failure_is_reported_and_can_be_retried_immediately()
    {
        using var client = factory.CreateTestClient();
        var email = Scenario.UniqueEmail();
        factory.Email.Fail = true;
        try
        {
            var response = await client.PostAsync("/api/registrations/email-code", new { email });
            await response.EnsureStatusAsync(HttpStatusCode.ServiceUnavailable);
            Assert.Equal("email_delivery_failed", (await response.ReadJsonAsync()).GetProperty("code").GetString());
        }
        finally
        {
            factory.Email.Fail = false;
        }

        // The undelivered code does not count towards the cooldown.
        var again = await client.PostAsync("/api/registrations/email-code", new { email });
        await again.EnsureStatusAsync(HttpStatusCode.Accepted);
        Assert.NotNull(factory.Email.LastRegistrationCodeFor(email));
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
