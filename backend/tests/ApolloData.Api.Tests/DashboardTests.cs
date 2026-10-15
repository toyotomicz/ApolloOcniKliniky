using System.Net;
using ApolloData.Api.Admin;
using ApolloData.Api.Tests.Infrastructure;

namespace ApolloData.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class DashboardTests(ApiFactory factory)
{
    [Fact]
    public async Task Dashboard_reflects_registrations_status_changes_and_sign_ins()
    {
        using var admin = factory.CreateTestClient();
        await Scenario.SignInAdminAsync(admin);
        var before = await (await admin.GetAsync("/api/admin/dashboard")).ReadJsonAsync();
        var m0 = before.GetProperty("metrics");

        using var clinicClient = factory.CreateTestClient();
        var (_, clinicId) = await Scenario.RegisterAndSignInAsync(factory, clinicClient);
        using var unverifiedClient = factory.CreateTestClient();
        await Scenario.RegisterAsync(factory, unverifiedClient);

        await (await clinicClient.PostAsync("/api/clinic/messages", new { body = "Dotaz k registraci" })).EnsureStatusAsync(HttpStatusCode.Created);

        var version = (await (await admin.GetAsync($"/api/admin/clinics/{clinicId}")).ReadJsonAsync()).GetProperty("version").GetUInt32();
        await (await admin.PostAsync($"/api/admin/clinics/{clinicId}/status", new { status = "approved", version }))
            .EnsureStatusAsync(HttpStatusCode.OK);

        var after = await (await admin.GetAsync("/api/admin/dashboard")).ReadJsonAsync();
        var m1 = after.GetProperty("metrics");

        int Delta(string name) => m1.GetProperty(name).GetInt32() - m0.GetProperty(name).GetInt32();
        Assert.Equal(2, Delta("totalClinics"));
        Assert.Equal(2, Delta("newToday"));
        Assert.Equal(1, Delta("pendingReview"));
        Assert.Equal(1, Delta("approved"));
        Assert.Equal(0, Delta("rejected"));
        Assert.Equal(1, Delta("uniqueClinicSignInsToday"));
        Assert.Equal(1, Delta("emailUnverified"));
        Assert.Equal(1, Delta("unreadMessages"));
        Assert.Equal(System.Text.Json.JsonValueKind.Number, m1.GetProperty("averageDecisionHours").ValueKind);

        int FunnelDelta(string name) => after.GetProperty("funnel").GetProperty(name).GetInt32() - before.GetProperty("funnel").GetProperty(name).GetInt32();
        Assert.Equal(2, FunnelDelta("registered"));
        Assert.Equal(1, FunnelDelta("emailVerified"));
        Assert.Equal(1, FunnelDelta("approved"));

        var plans = after.GetProperty("plans").EnumerateArray().Select(p => p.GetProperty("planCode").GetString()).ToList();
        Assert.Equal(["basic", "standard", "premium"], plans);

        Assert.Equal(7, after.GetProperty("seriesDays").GetInt32());
        var series = after.GetProperty("registrations");
        Assert.Equal(7, series.GetArrayLength());
        var todayCount = series[6].GetProperty("count").GetInt32();
        Assert.True(todayCount >= 2);

        var month = await (await admin.GetAsync("/api/admin/dashboard?days=30")).ReadJsonAsync();
        Assert.Equal(30, month.GetProperty("registrations").GetArrayLength());
        var unsupported = await (await admin.GetAsync("/api/admin/dashboard?days=12")).ReadJsonAsync();
        Assert.Equal(7, unsupported.GetProperty("seriesDays").GetInt32());

        var feed = after.GetProperty("recentActivity").EnumerateArray().Select(e => e.GetProperty("type").GetString()).ToList();
        Assert.Contains("clinicStatusChanged", feed);
        Assert.DoesNotContain("clinicSignedIn", feed);
    }

    [Fact]
    public void Daily_series_fills_missing_days_and_uses_local_day_boundaries()
    {
        var prague = TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague");
        var today = new DateOnly(2026, 10, 13);
        DateTime[] timestamps =
        [
            new(2026, 10, 12, 22, 30, 0, DateTimeKind.Utc), // 00:30 local on 13 Oct (CEST, UTC+2)
            new(2026, 10, 12, 21, 30, 0, DateTimeKind.Utc), // 23:30 local on 12 Oct
            new(2026, 10, 10, 8, 0, 0, DateTimeKind.Utc),
            new(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc),
            new(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc),    // outside the window, ignored
        ];

        var series = DashboardService.BuildDailySeries(timestamps, today, prague, 7);

        Assert.Equal(7, series.Count);
        Assert.Equal(new DateOnly(2026, 10, 7), series[0].Date);
        Assert.Equal(today, series[^1].Date);
        Assert.Equal([0, 0, 0, 2, 0, 1, 1], series.Select(d => d.Count));
    }
}
