using System.Net;
using System.Text.Json;
using ApolloData.Api.Auth;
using ApolloData.Api.Domain;
using ApolloData.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApolloData.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class AdminTests(ApiFactory factory)
{
    [Fact]
    public async Task Wrong_password_is_rejected_with_generic_message()
    {
        using var client = factory.CreateTestClient();
        var response = await client.PostAsync("/api/admin-auth/login", new { username = ApiFactory.AdminUsername, password = "wrong-password-123" });
        await response.EnsureStatusAsync(HttpStatusCode.Unauthorized);

        var unknown = await client.PostAsync("/api/admin-auth/login", new { username = "nobody", password = "wrong-password-123" });
        await unknown.EnsureStatusAsync(HttpStatusCode.Unauthorized);
        Assert.Equal(
            (await response.ReadJsonAsync()).GetProperty("detail").GetString(),
            (await unknown.ReadJsonAsync()).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Account_is_locked_after_repeated_failures_and_then_rejects_correct_password()
    {
        // Uses a separate administrator so the shared admin account is not locked for other tests.
        const string username = "lockout-test";
        const string password = "Lockout-Test-Password-1";
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AdministratorAccounts>()
                .ResetPasswordAsync(username, password, scope.ServiceProvider.GetRequiredService<SessionService>(), CancellationToken.None);
        }

        using var client = factory.CreateTestClient();
        for (var i = 0; i < 5; i++)
        {
            await (await client.PostAsync("/api/admin-auth/login", new { username, password = "bad-password-xyz" })).EnsureStatusAsync(HttpStatusCode.Unauthorized);
        }
        await (await client.PostAsync("/api/admin-auth/login", new { username, password })).EnsureStatusAsync(HttpStatusCode.Unauthorized);

        factory.Time.Advance(TimeSpan.FromMinutes(16));
        await (await client.PostAsync("/api/admin-auth/login", new { username, password })).EnsureStatusAsync(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Seeding_is_idempotent_and_does_not_reset_password()
    {
        var before = await factory.WithDbAsync(db => db.Administrators.SingleAsync(a => a.NormalizedUsername == ApiFactory.AdminUsername));

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<AdministratorAccounts>();
            await accounts.SeedAsync(ApiFactory.AdminUsername, "A-Completely-Different-Password", CancellationToken.None);
            await accounts.SeedAsync(ApiFactory.AdminUsername.ToUpperInvariant(), ApiFactory.AdminPassword, CancellationToken.None);
        }

        var admins = await factory.WithDbAsync(db => db.Administrators.Where(a => a.NormalizedUsername == ApiFactory.AdminUsername).ToListAsync());
        Assert.Single(admins);
        Assert.Equal(before.PasswordHash, admins[0].PasswordHash);
        Assert.DoesNotContain(ApiFactory.AdminPassword, admins[0].PasswordHash);
    }

    [Fact]
    public async Task Approval_updates_status_records_history_and_keeps_internal_note_private()
    {
        using var clinicClient = factory.CreateTestClient();
        var (email, clinicId) = await Scenario.RegisterAndSignInAsync(factory, clinicClient, "standard");

        using var admin = factory.CreateTestClient();
        await Scenario.SignInAdminAsync(admin);
        var detail = await (await admin.GetAsync($"/api/admin/clinics/{clinicId}")).ReadJsonAsync();
        var version = detail.GetProperty("version").GetUInt32();

        var response = await admin.PostAsync($"/api/admin/clinics/{clinicId}/status", new
        {
            status = "approved",
            message = "Vítejte, registrace je schválena.",
            internalNote = "INTERNAL-ONLY ověřeno telefonicky",
            version,
        });
        await response.EnsureStatusAsync(HttpStatusCode.OK);
        var updated = await response.ReadJsonAsync();
        Assert.Equal("approved", updated.GetProperty("profile").GetProperty("status").GetString());
        Assert.Equal(1, updated.GetProperty("statusHistory").GetArrayLength());
        Assert.Equal(1, updated.GetProperty("notes").GetArrayLength());

        var history = await factory.WithDbAsync(db => db.ClinicStatusChanges.Include(s => s.Administrator).SingleAsync(s => s.ClinicId == clinicId));
        Assert.Equal(ClinicStatus.Pending, history.FromStatus);
        Assert.Equal(ClinicStatus.Approved, history.ToStatus);
        Assert.Equal(ApiFactory.AdminUsername, history.Administrator!.Username);
        Assert.Equal("Vítejte, registrace je schválena.", history.Reason);

        // The clinic sees the new status and public message, but never the internal note.
        var clinicView = await clinicClient.GetAsync("/api/clinic/me");
        var raw = await clinicView.Content.ReadAsStringAsync();
        var me = JsonDocument.Parse(raw).RootElement;
        Assert.Equal("approved", me.GetProperty("status").GetString());
        Assert.Equal("Vítejte, registrace je schválena.", me.GetProperty("statusMessage").GetString());
        Assert.DoesNotContain("INTERNAL-ONLY", raw);
        Assert.NotNull(email);
    }

    [Fact]
    public async Task Rejection_requires_a_reason()
    {
        using var clinicClient = factory.CreateTestClient();
        var (_, clinicId) = await Scenario.RegisterAndSignInAsync(factory, clinicClient);
        using var admin = factory.CreateTestClient();
        await Scenario.SignInAdminAsync(admin);
        var version = (await (await admin.GetAsync($"/api/admin/clinics/{clinicId}")).ReadJsonAsync()).GetProperty("version").GetUInt32();

        var response = await admin.PostAsync($"/api/admin/clinics/{clinicId}/status", new { status = "rejected", version });

        await response.EnsureStatusAsync(HttpStatusCode.BadRequest);
        Assert.True((await response.ReadJsonAsync()).GetProperty("errors").TryGetProperty("message", out _));
    }

    [Fact]
    public async Task Stale_version_is_rejected_with_conflict_and_nothing_is_written()
    {
        using var clinicClient = factory.CreateTestClient();
        var (_, clinicId) = await Scenario.RegisterAndSignInAsync(factory, clinicClient);
        using var admin = factory.CreateTestClient();
        await Scenario.SignInAdminAsync(admin);
        var staleVersion = (await (await admin.GetAsync($"/api/admin/clinics/{clinicId}")).ReadJsonAsync()).GetProperty("version").GetUInt32();

        // Someone else changes the record in the meantime (here: the clinic edits its profile).
        await (await clinicClient.PutAsync("/api/clinic/me", new
        {
            contactFirstName = "Jana", contactLastName = "Nová", phone = "777123456", street = "Ulice 1", city = "Plzeň", postalCode = "30100",
        })).EnsureStatusAsync(HttpStatusCode.OK);

        var response = await admin.PostAsync($"/api/admin/clinics/{clinicId}/status",
            new { status = "rejected", message = "Neúplné údaje.", internalNote = "nemá se uložit", version = staleVersion });

        await response.EnsureStatusAsync(HttpStatusCode.Conflict);
        Assert.Equal(ClinicStatus.Pending, await factory.WithDbAsync(db => db.Clinics.Where(c => c.Id == clinicId).Select(c => c.Status).SingleAsync()));
        Assert.Equal(0, await factory.WithDbAsync(db => db.ClinicStatusChanges.CountAsync(s => s.ClinicId == clinicId)));
        Assert.Equal(0, await factory.WithDbAsync(db => db.AdminNotes.CountAsync(s => s.ClinicId == clinicId)));
    }

    [Fact]
    public async Task Admin_can_add_internal_note()
    {
        using var clinicClient = factory.CreateTestClient();
        var (_, clinicId) = await Scenario.RegisterAndSignInAsync(factory, clinicClient);
        using var admin = factory.CreateTestClient();
        await Scenario.SignInAdminAsync(admin);

        await (await admin.PostAsync($"/api/admin/clinics/{clinicId}/notes", new { text = "" })).EnsureStatusAsync(HttpStatusCode.BadRequest);
        await (await admin.PostAsync($"/api/admin/clinics/{clinicId}/notes", new { text = "Zavolat v pondělí." })).EnsureStatusAsync(HttpStatusCode.Created);

        var detail = await (await admin.GetAsync($"/api/admin/clinics/{clinicId}")).ReadJsonAsync();
        Assert.Equal("Zavolat v pondělí.", detail.GetProperty("notes")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task Clinic_list_supports_search_filters_and_pagination()
    {
        using var client = factory.CreateTestClient();
        var marker = Guid.NewGuid().ToString("N")[..10];
        for (var i = 0; i < 3; i++)
        {
            var email = Scenario.UniqueEmail();
            var body = Scenario.Registration(email, i == 0 ? "premium" : "basic", $"Klinika {marker} č. {i}");
            await (await client.PostAsync("/api/registrations", body)).EnsureStatusAsync(HttpStatusCode.Created);
        }

        using var admin = factory.CreateTestClient();
        await Scenario.SignInAdminAsync(admin);

        var all = await (await admin.GetAsync($"/api/admin/clinics?search={marker}&pageSize=2&sort=name&direction=asc")).ReadJsonAsync();
        Assert.Equal(3, all.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, all.GetProperty("totalPages").GetInt32());
        Assert.Equal(2, all.GetProperty("items").GetArrayLength());
        Assert.EndsWith("č. 0", all.GetProperty("items")[0].GetProperty("name").GetString());

        var page2 = await (await admin.GetAsync($"/api/admin/clinics?search={marker}&pageSize=2&page=2&sort=name&direction=asc")).ReadJsonAsync();
        Assert.Equal(1, page2.GetProperty("items").GetArrayLength());

        var premium = await (await admin.GetAsync($"/api/admin/clinics?search={marker}&plan=premium&status=pending")).ReadJsonAsync();
        Assert.Equal(1, premium.GetProperty("totalCount").GetInt32());

        var approved = await (await admin.GetAsync($"/api/admin/clinics?search={marker}&status=approved")).ReadJsonAsync();
        Assert.Equal(0, approved.GetProperty("totalCount").GetInt32());

        // LIKE wildcards in the search term are treated literally.
        var wildcard = await (await admin.GetAsync("/api/admin/clinics?search=%25%25%25")).ReadJsonAsync();
        Assert.Equal(0, wildcard.GetProperty("totalCount").GetInt32());

        await (await admin.GetAsync("/api/admin/clinics?status=unknown")).EnsureStatusAsync(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Admin_logout_revokes_session()
    {
        using var admin = factory.CreateTestClient();
        await Scenario.SignInAdminAsync(admin);
        await (await admin.GetAsync("/api/admin-auth/me")).EnsureStatusAsync(HttpStatusCode.OK);

        await (await admin.PostAsync("/api/admin-auth/logout", null)).EnsureStatusAsync(HttpStatusCode.NoContent);
        await (await admin.GetAsync("/api/admin-auth/me")).EnsureStatusAsync(HttpStatusCode.Unauthorized);
    }
}
