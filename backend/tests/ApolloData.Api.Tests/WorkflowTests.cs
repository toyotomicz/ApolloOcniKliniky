using System.Net;
using System.Text;
using ApolloData.Api.Domain;
using ApolloData.Api.Email;
using ApolloData.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApolloData.Api.Tests;

/// <summary>Status emails (outbox), bulk actions, clinic ↔ administrator messages and the PDF confirmation.</summary>
[Collection(ApiCollection.Name)]
public sealed class WorkflowTests(ApiFactory factory)
{
    private async Task<uint> VersionAsync(TestClient admin, Guid clinicId) =>
        (await (await admin.GetAsync($"/api/admin/clinics/{clinicId}")).ReadJsonAsync()).GetProperty("version").GetUInt32();

    [Fact]
    public async Task Rejection_queues_an_email_with_the_reason_that_the_worker_delivers()
    {
        using var clinicClient = factory.CreateTestClient();
        var (email, clinicId) = await Scenario.RegisterAndSignInAsync(factory, clinicClient);
        using var admin = factory.CreateTestClient();
        await Scenario.SignInAdminAsync(admin);

        var response = await admin.PostAsync($"/api/admin/clinics/{clinicId}/status", new
        {
            status = "rejected", message = "Doložte prosím oprávnění <oftalmologie>.", version = await VersionAsync(admin, clinicId),
        });
        await response.EnsureStatusAsync(HttpStatusCode.OK);

        var sent = await factory.Email.WaitForAsync(m => m.ToEmail == email && m.Subject.Contains("zamítnuta"));
        Assert.Contains("Doložte prosím oprávnění <oftalmologie>.", sent.TextBody);
        // User-provided text is HTML-encoded in the HTML part.
        Assert.Contains("&lt;oftalmologie&gt;", sent.HtmlBody);
        Assert.DoesNotContain("<oftalmologie>", sent.HtmlBody);

        var outbox = await factory.WithDbAsync(db => db.OutboundEmails.SingleAsync(e => e.ClinicId == clinicId));
        Assert.NotNull(outbox.SentAt);
        Assert.True(await factory.WithDbAsync(db =>
            db.ActivityEvents.AnyAsync(e => e.ClinicId == clinicId && e.Type == ActivityEventType.EmailSent)));
    }

    [Fact]
    public async Task Smtp_outage_does_not_lose_the_status_email_it_is_retried_later()
    {
        using var clinicClient = factory.CreateTestClient();
        var (email, clinicId) = await Scenario.RegisterAndSignInAsync(factory, clinicClient);
        using var admin = factory.CreateTestClient();
        await Scenario.SignInAdminAsync(admin);

        factory.Email.Fail = true;
        try
        {
            await (await admin.PostAsync($"/api/admin/clinics/{clinicId}/status", new { status = "approved", version = await VersionAsync(admin, clinicId) }))
                .EnsureStatusAsync(HttpStatusCode.OK);

            // The status change itself succeeded; the email waits in the outbox with a retry time.
            var queued = await WaitForOutboxAsync(clinicId, e => e.Attempts >= 1);
            Assert.Null(queued.SentAt);
            Assert.Null(queued.FailedAt);
            Assert.NotNull(queued.LastError);
            Assert.True(queued.NextAttemptAt > factory.Time.GetUtcNow().UtcDateTime);
        }
        finally
        {
            factory.Email.Fail = false;
        }

        factory.Time.Advance(TimeSpan.FromMinutes(2));
        await factory.Email.WaitForAsync(m => m.ToEmail == email && m.Subject.Contains("schválena"));
        var delivered = await WaitForOutboxAsync(clinicId, e => e.SentAt != null);
        Assert.Equal(2, delivered.Attempts);
    }

    [Fact]
    public async Task Demo_clinics_never_get_email()
    {
        using var clinicClient = factory.CreateTestClient();
        var (_, clinicId) = await Scenario.RegisterAndSignInAsync(factory, clinicClient);
        await factory.WithDbAsync(db => db.Clinics.Where(c => c.Id == clinicId).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDemo, true)));

        using var admin = factory.CreateTestClient();
        await Scenario.SignInAdminAsync(admin);
        await (await admin.PostAsync($"/api/admin/clinics/{clinicId}/status", new { status = "approved", version = await VersionAsync(admin, clinicId) }))
            .EnsureStatusAsync(HttpStatusCode.OK);

        Assert.False(await factory.WithDbAsync(db => db.OutboundEmails.AnyAsync(e => e.ClinicId == clinicId)));
    }

    [Fact]
    public async Task Bulk_status_change_updates_selected_clinics_in_one_go_and_reports_skipped_and_missing()
    {
        using var c1 = factory.CreateTestClient();
        using var c2 = factory.CreateTestClient();
        using var c3 = factory.CreateTestClient();
        var (_, first) = await Scenario.RegisterAndSignInAsync(factory, c1);
        var (_, second) = await Scenario.RegisterAndSignInAsync(factory, c2);
        var (_, third) = await Scenario.RegisterAndSignInAsync(factory, c3);
        using var admin = factory.CreateTestClient();
        await Scenario.SignInAdminAsync(admin);
        await (await admin.PostAsync($"/api/admin/clinics/{third}/status", new { status = "approved", version = await VersionAsync(admin, third) }))
            .EnsureStatusAsync(HttpStatusCode.OK);

        var missing = Guid.NewGuid();
        var response = await admin.PostAsync("/api/admin/clinics/bulk-status", new { clinicIds = new[] { first, second, third, missing }, status = "approved" });
        await response.EnsureStatusAsync(HttpStatusCode.OK);
        var result = await response.ReadJsonAsync();

        Assert.Equal(new[] { first, second }.Order(), result.GetProperty("updated").EnumerateArray().Select(e => e.GetGuid()).Order());
        Assert.Equal([third], result.GetProperty("skipped").EnumerateArray().Select(e => e.GetGuid()));
        Assert.Equal([missing], result.GetProperty("notFound").EnumerateArray().Select(e => e.GetGuid()));

        var statuses = await factory.WithDbAsync(db => db.Clinics.Where(c => c.Id == first || c.Id == second).Select(c => c.Status).ToListAsync());
        Assert.All(statuses, s => Assert.Equal(ClinicStatus.Approved, s));
        var bulkEvents = await factory.WithDbAsync(db => db.ActivityEvents
            .CountAsync(e => (e.ClinicId == first || e.ClinicId == second) && e.Type == ActivityEventType.ClinicStatusChanged && e.Detail!.EndsWith(";bulk")));
        Assert.Equal(2, bulkEvents);
        Assert.Equal(2, await factory.WithDbAsync(db => db.ClinicStatusChanges.CountAsync(s => s.ClinicId == first || s.ClinicId == second)));
    }

    [Fact]
    public async Task Bulk_rejection_requires_a_reason_and_limits_the_batch_size()
    {
        using var admin = factory.CreateTestClient();
        await Scenario.SignInAdminAsync(admin);

        var noReason = await admin.PostAsync("/api/admin/clinics/bulk-status", new { clinicIds = new[] { Guid.NewGuid() }, status = "rejected" });
        await noReason.EnsureStatusAsync(HttpStatusCode.BadRequest);
        Assert.True((await noReason.ReadJsonAsync()).GetProperty("errors").TryGetProperty("message", out _));

        var tooMany = await admin.PostAsync("/api/admin/clinics/bulk-status",
            new { clinicIds = Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()), status = "approved" });
        await tooMany.EnsureStatusAsync(HttpStatusCode.BadRequest);

        using var clinicClient = factory.CreateTestClient();
        await Scenario.RegisterAndSignInAsync(factory, clinicClient);
        var asClinic = await clinicClient.PostAsync("/api/admin/clinics/bulk-status", new { clinicIds = new[] { Guid.NewGuid() }, status = "approved" });
        await asClinic.EnsureStatusAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Clinic_and_administrator_exchange_messages_with_unread_tracking()
    {
        using var clinicClient = factory.CreateTestClient();
        var (email, clinicId) = await Scenario.RegisterAndSignInAsync(factory, clinicClient);
        using var admin = factory.CreateTestClient();
        await Scenario.SignInAdminAsync(admin);

        await (await clinicClient.PostAsync("/api/clinic/messages", new { body = "  " })).EnsureStatusAsync(HttpStatusCode.BadRequest);
        await (await clinicClient.PostAsync("/api/clinic/messages", new { body = "Kdy bude registrace posouzena?" })).EnsureStatusAsync(HttpStatusCode.Created);

        var list = await admin.GetJsonAsync<System.Text.Json.JsonElement>($"/api/admin/clinics?unread=true&search={Uri.EscapeDataString(email)}");
        var item = Assert.Single(list.GetProperty("items").EnumerateArray());
        Assert.Equal(1, item.GetProperty("unreadMessages").GetInt32());

        // Opening the thread marks the clinic's messages as read.
        var thread = await admin.GetJsonAsync<System.Text.Json.JsonElement>($"/api/admin/clinics/{clinicId}/messages");
        Assert.Equal("Kdy bude registrace posouzena?", Assert.Single(thread.EnumerateArray()).GetProperty("body").GetString());
        var after = await admin.GetJsonAsync<System.Text.Json.JsonElement>($"/api/admin/clinics?unread=true&search={Uri.EscapeDataString(email)}");
        Assert.Empty(after.GetProperty("items").EnumerateArray());

        var reply = await admin.PostAsync($"/api/admin/clinics/{clinicId}/messages", new { body = "Do zítřka, děkujeme za trpělivost." });
        await reply.EnsureStatusAsync(HttpStatusCode.Created);
        Assert.Equal(ApiFactory.AdminUsername, (await reply.ReadJsonAsync()).GetProperty("authorName").GetString());

        // The clinic gets a notification email without the message text, and sees the reply without the admin's username.
        var notification = await factory.Email.WaitForAsync(m => m.ToEmail == email && m.Subject.Contains("nová zpráva"));
        Assert.DoesNotContain("Do zítřka", notification.TextBody + notification.HtmlBody);

        var clinicThread = (await clinicClient.GetJsonAsync<System.Text.Json.JsonElement>("/api/clinic/messages")).EnumerateArray().ToList();
        Assert.Equal(2, clinicThread.Count);
        Assert.Equal("Administrátor", clinicThread[1].GetProperty("authorName").GetString());
        Assert.Equal(JsonNull, clinicThread[1].GetProperty("readAt").ValueKind);
        var reread = (await clinicClient.GetJsonAsync<System.Text.Json.JsonElement>("/api/clinic/messages")).EnumerateArray().ToList();
        Assert.NotEqual(JsonNull, reread[1].GetProperty("readAt").ValueKind);

        await (await factory.CreateTestClient().GetAsync("/api/clinic/messages")).EnsureStatusAsync(HttpStatusCode.Unauthorized);
    }

    private const System.Text.Json.JsonValueKind JsonNull = System.Text.Json.JsonValueKind.Null;

    [Fact]
    public async Task Clinic_and_administrator_can_download_the_pdf_confirmation()
    {
        using var clinicClient = factory.CreateTestClient();
        var (_, clinicId) = await Scenario.RegisterAndSignInAsync(factory, clinicClient, "premium");

        var response = await clinicClient.GetAsync("/api/clinic/me/confirmation.pdf");
        await response.EnsureStatusAsync(HttpStatusCode.OK);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("potvrzeni-registrace-REG-", response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));

        using var admin = factory.CreateTestClient();
        await Scenario.SignInAdminAsync(admin);
        await (await admin.GetAsync($"/api/admin/clinics/{clinicId}/confirmation.pdf")).EnsureStatusAsync(HttpStatusCode.OK);
        await (await admin.GetAsync($"/api/admin/clinics/{Guid.NewGuid()}/confirmation.pdf")).EnsureStatusAsync(HttpStatusCode.NotFound);

        using var anonymous = factory.CreateTestClient();
        await (await anonymous.GetAsync("/api/clinic/me/confirmation.pdf")).EnsureStatusAsync(HttpStatusCode.Unauthorized);

        Assert.Equal(2, await factory.WithDbAsync(db => db.ActivityEvents.CountAsync(e => e.ClinicId == clinicId && e.Type == ActivityEventType.ConfirmationDownloaded)));
    }

    private async Task<OutboundEmail> WaitForOutboxAsync(Guid clinicId, Func<OutboundEmail, bool> predicate)
    {
        for (var i = 0; i < 200; i++)
        {
            var rows = await factory.WithDbAsync(db => db.OutboundEmails.AsNoTracking().Where(e => e.ClinicId == clinicId).ToListAsync());
            if (rows.FirstOrDefault(predicate) is { } row) return row;
            await Task.Delay(50);
        }
        throw new TimeoutException("Outbox row did not reach the expected state.");
    }
}
