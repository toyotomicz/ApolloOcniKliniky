using System.Net;
using System.Text;
using ApolloData.Api.Domain;
using ApolloData.Api.Geo;
using ApolloData.Api.Hosting;
using ApolloData.Api.Realtime;
using ApolloData.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApolloData.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class AuditMapRealtimeTests(ApiFactory factory)
{
    [Fact]
    public async Task Audit_log_filters_events_and_exports_csv_safe_for_spreadsheets()
    {
        using var clinicClient = factory.CreateTestClient();
        var email = Scenario.UniqueEmail();
        var body = Scenario.Registration(email, name: "=HYPERLINK(\"http://evil.test\")");
        await (await Scenario.SubmitRegistrationAsync(factory, clinicClient, body)).EnsureStatusAsync(HttpStatusCode.Created);
        var clinicId = await factory.WithDbAsync(db => db.Clinics.Where(c => c.NormalizedEmail == email).Select(c => c.Id).SingleAsync());

        using var admin = factory.CreateTestClient();
        await Scenario.SignInAdminAsync(admin);

        var page = await admin.GetJsonAsync<System.Text.Json.JsonElement>($"/api/admin/audit?clinicId={clinicId}&actor=clinic&type=clinicRegistered");
        var item = Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.Equal("clinicRegistered", item.GetProperty("type").GetString());
        Assert.Equal("clinic", item.GetProperty("actor").GetString());

        var adminEvents = await admin.GetJsonAsync<System.Text.Json.JsonElement>("/api/admin/audit?type=adminSignedIn&pageSize=5");
        Assert.All(adminEvents.GetProperty("items").EnumerateArray(), e => Assert.Equal("administrator", e.GetProperty("actor").GetString()));
        await (await admin.GetAsync("/api/admin/audit?type=nonsense")).EnsureStatusAsync(HttpStatusCode.BadRequest);

        var export = await admin.GetAsync($"/api/admin/audit/export?clinicId={clinicId}");
        await export.EnsureStatusAsync(HttpStatusCode.OK);
        Assert.Equal("text/csv", export.Content.Headers.ContentType?.MediaType);
        var bytes = await export.Content.ReadAsByteArrayAsync();
        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
        var csv = Encoding.UTF8.GetString(bytes[3..]);
        Assert.StartsWith("Čas;Událost;Kdo;Jméno;Klinika;ID kliniky;Detail", csv);
        Assert.Contains("\"Registrace kliniky\"", csv);
        // The formula-like clinic name is neutralised with a leading apostrophe.
        Assert.Contains("\"'=HYPERLINK(\"\"http://evil.test\"\")\"", csv);

        Assert.True(await factory.WithDbAsync(db => db.ActivityEvents.AnyAsync(e => e.Type == ActivityEventType.AuditLogExported)));
        using var anonymous = factory.CreateTestClient();
        await (await anonymous.GetAsync("/api/admin/audit/export")).EnsureStatusAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Geocoded_clinics_appear_on_the_map_and_an_address_change_resolves_them_again()
    {
        using var clinicClient = factory.CreateTestClient();
        var (_, clinicId) = await Scenario.RegisterAndSignInAsync(factory, clinicClient);
        var worker = factory.Services.GetRequiredService<GeocodingWorker>();
        factory.Geocoder.Result = new GeoPoint(49.7475, 13.3776);

        using var admin = factory.CreateTestClient();
        await Scenario.SignInAdminAsync(admin);
        var before = await admin.GetJsonAsync<System.Text.Json.JsonElement>("/api/admin/clinics/map");
        Assert.DoesNotContain(before.GetProperty("items").EnumerateArray(), c => c.GetProperty("id").GetGuid() == clinicId);

        // The worker resolves one clinic per call, newest first; drain until ours has coordinates.
        for (var i = 0; i < 500 && await factory.WithDbAsync(db => db.Clinics.AnyAsync(c => c.Id == clinicId && c.Latitude == null)); i++)
        {
            Assert.True(await worker.ProcessNextAsync(CancellationToken.None));
        }
        var map = await admin.GetJsonAsync<System.Text.Json.JsonElement>("/api/admin/clinics/map?status=pending");
        var pin = Assert.Single(map.GetProperty("items").EnumerateArray(), c => c.GetProperty("id").GetGuid() == clinicId);
        Assert.Equal(49.7475, pin.GetProperty("latitude").GetDouble(), 4);

        var profile = await clinicClient.GetJsonAsync<System.Text.Json.JsonElement>("/api/clinic/me");
        var update = new
        {
            contactFirstName = profile.GetProperty("contactFirstName").GetString(),
            contactLastName = profile.GetProperty("contactLastName").GetString(),
            phone = profile.GetProperty("phone").GetString(),
            street = "Americká 42",
            city = "Plzeň",
            postalCode = "30100",
            publicPhones = new[] { "377 123 456" },
        };
        await (await clinicClient.PutAsync("/api/clinic/me", update)).EnsureStatusAsync(HttpStatusCode.OK);
        var reset = await factory.WithDbAsync(db => db.Clinics.SingleAsync(c => c.Id == clinicId));
        Assert.Null(reset.Latitude);
        Assert.Null(reset.GeocodedAt);
    }

    [Fact]
    public async Task Realtime_hub_requires_an_administrator_and_announces_new_registrations()
    {
        using var anonymous = factory.CreateTestClient();
        var negotiate = await anonymous.Http.PostAsync("/hubs/admin/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.Unauthorized, negotiate.StatusCode);

        using var admin = factory.CreateTestClient();
        await Scenario.SignInAdminAsync(admin);
        await using var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(TestClient.BaseUri, AdminHub.Path), o =>
            {
                o.Transports = HttpTransportType.LongPolling;
                o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                o.Headers["Cookie"] = admin.CookieHeader;
            })
            .Build();

        var received = new TaskCompletionSource<RegistrationCreatedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<RegistrationCreatedEvent>(AdminEvents.RegistrationCreated, e => received.TrySetResult(e));
        await connection.StartAsync();

        using var clinicClient = factory.CreateTestClient();
        var email = await Scenario.RegisterAsync(factory, clinicClient, "standard");

        var announced = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("standard", announced.PlanCode);
        Assert.Equal(await factory.WithDbAsync(db => db.Clinics.Where(c => c.NormalizedEmail == email).Select(c => c.Id).SingleAsync()), announced.ClinicId);
    }

    [Fact]
    public async Task Demo_data_is_seeded_once_with_locations_and_without_email_and_can_be_removed()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var seeder = scope.ServiceProvider.GetRequiredService<DemoDataSeeder>();
        var emailsBefore = await factory.WithDbAsync(db => db.OutboundEmails.CountAsync());
        try
        {
            Assert.Equal(DemoDataSeeder.ClinicCount, await seeder.SeedAsync(CancellationToken.None));

            var demo = await factory.WithDbAsync(db => db.Clinics.Where(c => c.IsDemo).ToListAsync());
            Assert.Equal(DemoDataSeeder.ClinicCount, demo.Count);
            Assert.All(demo, c =>
            {
                Assert.EndsWith("@" + DemoDataSeeder.EmailDomain, c.Email);
                Assert.NotNull(c.Latitude);
                Assert.True(c.OrganizationId is null || Api.Common.Rules.IsValidIco(c.OrganizationId));
            });
            Assert.Contains(demo, c => c.Status == ClinicStatus.Approved);
            Assert.Contains(demo, c => c.Status == ClinicStatus.Rejected);
            Assert.Contains(demo, c => c.Status == ClinicStatus.Pending);
            Assert.Equal(emailsBefore, await factory.WithDbAsync(db => db.OutboundEmails.CountAsync()));
        }
        finally
        {
            Assert.Equal(DemoDataSeeder.ClinicCount, await seeder.RemoveAsync(CancellationToken.None));
        }
        Assert.False(await factory.WithDbAsync(db => db.Clinics.AnyAsync(c => c.IsDemo)));
    }
}
