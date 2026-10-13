using System.Net;
using ApolloData.Api.Domain;
using ApolloData.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ApolloData.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class AuthorizationTests(ApiFactory factory)
{
    public static TheoryData<string> AdminGetEndpoints =>
    [
        "/api/admin/dashboard",
        "/api/admin/clinics",
        $"/api/admin/clinics/{Guid.NewGuid()}",
        "/api/admin-auth/me",
    ];

    [Theory]
    [MemberData(nameof(AdminGetEndpoints))]
    public async Task Anonymous_requests_to_admin_endpoints_are_unauthorized(string url)
    {
        using var client = factory.CreateTestClient();
        await (await client.GetAsync(url)).EnsureStatusAsync(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [MemberData(nameof(AdminGetEndpoints))]
    public async Task Clinic_session_cannot_access_admin_endpoints(string url)
    {
        using var client = factory.CreateTestClient();
        await Scenario.RegisterAndSignInAsync(factory, client);

        // The clinic cookie is not valid for the admin scheme at all.
        await (await client.GetAsync(url)).EnsureStatusAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Clinic_cannot_change_status_through_admin_endpoint()
    {
        using var client = factory.CreateTestClient();
        var (_, clinicId) = await Scenario.RegisterAndSignInAsync(factory, client);

        var response = await client.PostAsync($"/api/admin/clinics/{clinicId}/status", new { status = "approved", version = 0 });

        await response.EnsureStatusAsync(HttpStatusCode.Unauthorized);
        var status = await factory.WithDbAsync(db => db.Clinics.Where(c => c.Id == clinicId).Select(c => c.Status).SingleAsync());
        Assert.Equal(ClinicStatus.Pending, status);
    }

    [Fact]
    public async Task Anonymous_requests_to_clinic_portal_are_unauthorized()
    {
        using var client = factory.CreateTestClient();
        await (await client.GetAsync("/api/clinic/me")).EnsureStatusAsync(HttpStatusCode.Unauthorized);
        await (await client.PutAsync("/api/clinic/me", new { })).EnsureStatusAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Each_clinic_only_sees_and_edits_its_own_record()
    {
        using var clientA = factory.CreateTestClient();
        using var clientB = factory.CreateTestClient();
        var (emailA, idA) = await Scenario.RegisterAndSignInAsync(factory, clientA);
        var (emailB, idB) = await Scenario.RegisterAndSignInAsync(factory, clientB, "standard");

        var meA = await (await clientA.GetAsync("/api/clinic/me")).ReadJsonAsync();
        var meB = await (await clientB.GetAsync("/api/clinic/me")).ReadJsonAsync();
        Assert.Equal(idA, meA.GetProperty("id").GetGuid());
        Assert.Equal(emailA, meA.GetProperty("email").GetString());
        Assert.Equal(idB, meB.GetProperty("id").GetGuid());
        Assert.Equal(emailB, meB.GetProperty("email").GetString());

        // Clinic A tries to smuggle another clinic's id / privileged fields into its update.
        var update = await clientA.PutAsync("/api/clinic/me", new
        {
            id = idB,
            status = "approved",
            email = "hijack@example.test",
            planCode = "premium",
            contactFirstName = "Eva",
            contactLastName = "Malá",
            phone = "777000111",
            street = "Nová 5",
            city = "Brno",
            postalCode = "60200",
        });
        await update.EnsureStatusAsync(HttpStatusCode.OK);

        var (a, b) = await factory.WithDbAsync(async db =>
            (await db.Clinics.SingleAsync(c => c.Id == idA), await db.Clinics.SingleAsync(c => c.Id == idB)));
        Assert.Equal("Eva", a.ContactFirstName);
        Assert.Equal("602 00", a.PostalCode);
        Assert.Equal(ClinicStatus.Pending, a.Status);
        Assert.Equal(emailA, a.Email);
        Assert.Equal("basic", a.PlanCode);
        Assert.Equal("Jana", b.ContactFirstName);
    }

    [Fact]
    public async Task Admin_session_is_not_a_clinic_session()
    {
        using var client = factory.CreateTestClient();
        await Scenario.SignInAdminAsync(client);
        await (await client.GetAsync("/api/clinic/me")).EnsureStatusAsync(HttpStatusCode.Unauthorized);
    }
}
