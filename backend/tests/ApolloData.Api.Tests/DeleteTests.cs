using System.Net;
using ApolloData.Api.Domain;
using ApolloData.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ApolloData.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class DeleteTests(ApiFactory factory)
{
    [Fact]
    public async Task Deleting_a_clinic_removes_its_data_and_session_and_is_audited()
    {
        using var clinicClient = factory.CreateTestClient();
        var (_, clinicId) = await Scenario.RegisterAndSignInAsync(factory, clinicClient);
        await (await clinicClient.PostAsync("/api/clinic/messages", new { body = "Dotaz" })).EnsureStatusAsync(HttpStatusCode.Created);

        using var admin = factory.CreateTestClient();
        await Scenario.SignInAdminAsync(admin);
        await (await admin.PostAsync($"/api/admin/clinics/{clinicId}/notes", new { text = "Poznámka" })).EnsureStatusAsync(HttpStatusCode.Created);

        // Clinics cannot delete anything through the admin API.
        using (var asClinic = new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/clinics/{clinicId}"))
        {
            asClinic.Headers.Add("X-CSRF-TOKEN", await clinicClient.CsrfTokenAsync());
            await (await clinicClient.Http.SendAsync(asClinic)).EnsureStatusAsync(HttpStatusCode.Unauthorized);
        }

        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/clinics/{clinicId}");
        request.Headers.Add("X-CSRF-TOKEN", await admin.CsrfTokenAsync());
        await (await admin.Http.SendAsync(request)).EnsureStatusAsync(HttpStatusCode.NoContent);

        Assert.False(await factory.WithDbAsync(db => db.Clinics.AnyAsync(c => c.Id == clinicId)));
        Assert.False(await factory.WithDbAsync(db => db.ClinicMessages.AnyAsync(m => m.ClinicId == clinicId)));
        Assert.False(await factory.WithDbAsync(db => db.AdminNotes.AnyAsync(n => n.ClinicId == clinicId)));
        Assert.True(await factory.WithDbAsync(db => db.ActivityEvents.AnyAsync(e => e.Type == ActivityEventType.ClinicDeleted && e.Detail == "Ordinace Testovací s.r.o.")));
        // The clinic's session is gone with it.
        await (await clinicClient.GetAsync("/api/clinic/me")).EnsureStatusAsync(HttpStatusCode.Unauthorized);

        await (await admin.GetAsync($"/api/admin/clinics/{clinicId}")).EnsureStatusAsync(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Bulk_delete_reports_deleted_and_missing_clinics()
    {
        using var c1 = factory.CreateTestClient();
        using var c2 = factory.CreateTestClient();
        var (_, first) = await Scenario.RegisterAndSignInAsync(factory, c1);
        var (_, second) = await Scenario.RegisterAndSignInAsync(factory, c2);
        using var admin = factory.CreateTestClient();
        await Scenario.SignInAdminAsync(admin);

        var missing = Guid.NewGuid();
        var response = await admin.PostAsync("/api/admin/clinics/bulk-delete", new { clinicIds = new[] { first, second, missing } });
        await response.EnsureStatusAsync(HttpStatusCode.OK);
        var json = await response.ReadJsonAsync();
        Assert.Equal(2, json.GetProperty("deleted").GetArrayLength());
        Assert.Equal(missing, Assert.Single(json.GetProperty("notFound").EnumerateArray()).GetGuid());
        Assert.False(await factory.WithDbAsync(db => db.Clinics.AnyAsync(c => c.Id == first || c.Id == second)));

        await (await admin.PostAsync("/api/admin/clinics/bulk-delete", new { clinicIds = Array.Empty<Guid>() })).EnsureStatusAsync(HttpStatusCode.BadRequest);
    }
}
