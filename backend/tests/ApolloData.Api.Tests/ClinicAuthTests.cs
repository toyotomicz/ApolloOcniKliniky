using System.Net;
using ApolloData.Api.Domain;
using ApolloData.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ApolloData.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class ClinicAuthTests(ApiFactory factory)
{
    [Fact]
    public async Task Valid_code_signs_in_and_verifies_email_without_approving()
    {
        using var client = factory.CreateTestClient();
        var email = await Scenario.RegisterAsync(factory, client);
        var code = factory.Email.LastCodeFor(email);

        var verify = await client.PostAsync("/api/clinic-auth/verify-code", new { email, code });
        await verify.EnsureStatusAsync(HttpStatusCode.OK);

        var me = await client.GetJsonAsync<Profile>("/api/clinic/me");
        Assert.Equal(email, me.Email);
        Assert.NotNull(me.EmailVerifiedAt);
        Assert.Equal("pending", me.Status);
    }

    [Fact]
    public async Task Code_is_stored_only_as_hash()
    {
        using var client = factory.CreateTestClient();
        var email = await Scenario.RegisterAsync(factory, client);
        var code = factory.Email.LastCodeFor(email)!;

        var challenge = await factory.WithDbAsync(db => db.ClinicLoginChallenges.SingleAsync(c => c.Clinic!.NormalizedEmail == email));
        Assert.Equal(32, challenge.CodeHash.Length);
        Assert.DoesNotContain(code, Convert.ToHexString(challenge.CodeHash));
    }

    [Fact]
    public async Task Code_cannot_be_reused()
    {
        using var client = factory.CreateTestClient();
        var email = await Scenario.RegisterAsync(factory, client);
        var code = factory.Email.LastCodeFor(email);

        await (await client.PostAsync("/api/clinic-auth/verify-code", new { email, code })).EnsureStatusAsync(HttpStatusCode.OK);

        using var other = factory.CreateTestClient();
        var reuse = await other.PostAsync("/api/clinic-auth/verify-code", new { email, code });
        await reuse.EnsureStatusAsync(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Expired_code_is_rejected()
    {
        using var client = factory.CreateTestClient();
        var email = await Scenario.RegisterAsync(factory, client);
        var code = factory.Email.LastCodeFor(email);

        factory.Time.Advance(TimeSpan.FromMinutes(11));

        var response = await client.PostAsync("/api/clinic-auth/verify-code", new { email, code });
        await response.EnsureStatusAsync(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task New_code_supersedes_previous_code()
    {
        using var client = factory.CreateTestClient();
        var email = await Scenario.RegisterAsync(factory, client);
        var firstCode = factory.Email.LastCodeFor(email);

        factory.Time.Advance(TimeSpan.FromSeconds(61));
        await (await client.PostAsync("/api/clinic-auth/request-code", new { email })).EnsureStatusAsync(HttpStatusCode.Accepted);
        var secondCode = factory.Email.LastCodeFor(email);
        Assert.Equal(2, factory.Email.CountFor(email));

        if (firstCode != secondCode)
        {
            var old = await client.PostAsync("/api/clinic-auth/verify-code", new { email, code = firstCode });
            await old.EnsureStatusAsync(HttpStatusCode.BadRequest);
        }
        var current = await client.PostAsync("/api/clinic-auth/verify-code", new { email, code = secondCode });
        await current.EnsureStatusAsync(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Resend_within_cooldown_does_not_send_another_email_but_responds_identically()
    {
        using var client = factory.CreateTestClient();
        var email = await Scenario.RegisterAsync(factory, client);

        var response = await client.PostAsync("/api/clinic-auth/request-code", new { email });

        await response.EnsureStatusAsync(HttpStatusCode.Accepted);
        Assert.Equal(1, factory.Email.CountFor(email));
    }

    [Fact]
    public async Task Code_is_locked_after_max_wrong_attempts()
    {
        using var client = factory.CreateTestClient();
        var email = await Scenario.RegisterAsync(factory, client);
        var code = factory.Email.LastCodeFor(email)!;
        var wrong = code == "000000" ? "111111" : "000000";

        for (var i = 0; i < 5; i++)
        {
            await (await client.PostAsync("/api/clinic-auth/verify-code", new { email, code = wrong })).EnsureStatusAsync(HttpStatusCode.BadRequest);
        }

        var correct = await client.PostAsync("/api/clinic-auth/verify-code", new { email, code });
        await correct.EnsureStatusAsync(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Unknown_email_gets_same_public_response_and_no_email()
    {
        using var client = factory.CreateTestClient();
        var known = await Scenario.RegisterAsync(factory, client);
        factory.Time.Advance(TimeSpan.FromSeconds(61));
        var unknown = Scenario.UniqueEmail("neexistuje");
        var before = factory.Email.Sent.Count;

        var knownResponse = await client.PostAsync("/api/clinic-auth/request-code", new { email = known });
        var unknownResponse = await client.PostAsync("/api/clinic-auth/request-code", new { email = unknown });

        Assert.Equal(knownResponse.StatusCode, unknownResponse.StatusCode);
        Assert.Equal(await knownResponse.Content.ReadAsStringAsync(), await unknownResponse.Content.ReadAsStringAsync());
        Assert.Equal(before + 1, factory.Email.Sent.Count);
        Assert.Equal(0, factory.Email.CountFor(unknown));
    }

    [Fact]
    public async Task Logout_revokes_the_server_side_session()
    {
        using var client = factory.CreateTestClient();
        await Scenario.RegisterAndSignInAsync(factory, client);

        // Capture the cookie as an attacker might, then log out.
        var stolen = client.CookieHeader;
        Assert.Contains("apollo_clinic=", stolen);

        await (await client.PostAsync("/api/clinic-auth/logout", null)).EnsureStatusAsync(HttpStatusCode.NoContent);
        await (await client.GetAsync("/api/clinic/me")).EnsureStatusAsync(HttpStatusCode.Unauthorized);

        // Replaying the old cookie must not work either, because the session row is revoked.
        using var replay = factory.CreateClient(new() { BaseAddress = TestClient.BaseUri, HandleCookies = false });
        replay.DefaultRequestHeaders.Add("Cookie", stolen);
        var response = await replay.GetAsync("/api/clinic/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Verification_records_activity_and_session()
    {
        using var client = factory.CreateTestClient();
        var (_, clinicId) = await Scenario.RegisterAndSignInAsync(factory, client);

        var types = await factory.WithDbAsync(db => db.ActivityEvents.Where(e => e.ClinicId == clinicId).Select(e => e.Type).ToListAsync());
        Assert.Contains(ActivityEventType.ClinicRegistered, types);
        Assert.Contains(ActivityEventType.ClinicEmailVerified, types);
        Assert.Contains(ActivityEventType.ClinicSignedIn, types);
        Assert.Equal(1, await factory.WithDbAsync(db => db.UserSessions.CountAsync(s => s.ClinicId == clinicId && s.RevokedAt == null)));
    }

    private sealed record Profile(string Email, DateTime? EmailVerifiedAt, string Status);
}
