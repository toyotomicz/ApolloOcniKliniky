using System.Net;
using ApolloData.Api.Auth;
using ApolloData.Api.Domain;
using ApolloData.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OtpNet;

namespace ApolloData.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class TwoFactorTests(ApiFactory factory)
{
    private const string Password = "Two-Factor-Test-Password-1";

    /// <summary>Uses its own administrator so the shared admin account keeps signing in with a password only.</summary>
    private async Task<string> CreateAdminAsync()
    {
        var username = $"tfa-{Guid.NewGuid():N}"[..20];
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AdministratorAccounts>()
            .ResetPasswordAsync(username, Password, scope.ServiceProvider.GetRequiredService<SessionService>(), CancellationToken.None);
        return username;
    }

    private string CodeFor(string secret) =>
        new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp(factory.Time.GetUtcNow().UtcDateTime);

    private static async Task SignInWithPasswordAsync(TestClient client, string username, HttpStatusCode expected)
    {
        var response = await client.PostAsync("/api/admin-auth/login", new { username, password = Password });
        await response.EnsureStatusAsync(expected);
    }

    private async Task<(string Secret, List<string> RecoveryCodes)> EnableAsync(TestClient admin)
    {
        var setup = await (await admin.PostAsync("/api/admin/security/two-factor/setup", new { })).ReadJsonAsync();
        var secret = setup.GetProperty("secret").GetString()!.Replace(" ", "");
        Assert.StartsWith("otpauth://totp/", setup.GetProperty("otpAuthUri").GetString());
        Assert.StartsWith("data:image/png;base64,", setup.GetProperty("qrCodeDataUri").GetString());

        await (await admin.PostAsync("/api/admin/security/two-factor/enable", new { code = "000000" })).EnsureStatusAsync(HttpStatusCode.BadRequest);
        var enable = await admin.PostAsync("/api/admin/security/two-factor/enable", new { code = CodeFor(secret) });
        await enable.EnsureStatusAsync(HttpStatusCode.OK);
        var codes = (await enable.ReadJsonAsync()).GetProperty("recoveryCodes").EnumerateArray().Select(c => c.GetString()!).ToList();
        Assert.Equal(TwoFactorService.RecoveryCodeCount, codes.Count);
        return (secret, codes);
    }

    [Fact]
    public async Task Enabled_two_factor_requires_a_current_code_after_the_password_and_rejects_replay()
    {
        var username = await CreateAdminAsync();
        using var admin = factory.CreateTestClient();
        await SignInWithPasswordAsync(admin, username, HttpStatusCode.OK);
        var (secret, _) = await EnableAsync(admin);

        var stored = await factory.WithDbAsync(db => db.Administrators.SingleAsync(a => a.Username == username));
        Assert.True(stored.TwoFactorEnabled);
        Assert.DoesNotContain(secret, stored.TwoFactorSecret); // encrypted at rest

        using var client = factory.CreateTestClient();
        // Password alone no longer creates a session.
        await SignInWithPasswordAsync(client, username, HttpStatusCode.Accepted);
        await (await client.GetAsync("/api/admin-auth/me")).EnsureStatusAsync(HttpStatusCode.Unauthorized);

        await (await client.PostAsync("/api/admin-auth/login/two-factor", new { code = "123456" })).EnsureStatusAsync(HttpStatusCode.Unauthorized);

        // The code used to enable 2FA was consumed: a new time step is needed.
        factory.Time.Advance(TimeSpan.FromSeconds(30));
        var code = CodeFor(secret);
        var ok = await client.PostAsync("/api/admin-auth/login/two-factor", new { code });
        await ok.EnsureStatusAsync(HttpStatusCode.OK);
        Assert.True((await ok.ReadJsonAsync()).GetProperty("twoFactorEnabled").GetBoolean());
        await (await client.GetAsync("/api/admin-auth/me")).EnsureStatusAsync(HttpStatusCode.OK);

        // Replaying the same code in another login is rejected.
        using var attacker = factory.CreateTestClient();
        await SignInWithPasswordAsync(attacker, username, HttpStatusCode.Accepted);
        await (await attacker.PostAsync("/api/admin-auth/login/two-factor", new { code })).EnsureStatusAsync(HttpStatusCode.Unauthorized);

        var events = await factory.WithDbAsync(db => db.ActivityEvents
            .Where(e => e.AdministratorId == stored.Id).Select(e => new { e.Type, e.Detail }).ToListAsync());
        Assert.Contains(events, e => e.Type == ActivityEventType.AdminTwoFactorEnabled);
        Assert.Contains(events, e => e.Type == ActivityEventType.AdminSignedIn && e.Detail == "2fa");
        Assert.Contains(events, e => e.Type == ActivityEventType.AdminSignInFailed && e.Detail == "2fa");
    }

    [Fact]
    public async Task Second_step_without_a_valid_pending_login_is_rejected()
    {
        using var client = factory.CreateTestClient();
        await (await client.PostAsync("/api/admin-auth/login/two-factor", new { code = "123456" })).EnsureStatusAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Recovery_code_signs_in_once_and_disabling_requires_a_code()
    {
        var username = await CreateAdminAsync();
        using var admin = factory.CreateTestClient();
        await SignInWithPasswordAsync(admin, username, HttpStatusCode.OK);
        var (secret, recoveryCodes) = await EnableAsync(admin);

        using var client = factory.CreateTestClient();
        await SignInWithPasswordAsync(client, username, HttpStatusCode.Accepted);
        await (await client.PostAsync("/api/admin-auth/login/two-factor", new { recoveryCode = recoveryCodes[0].ToLowerInvariant() }))
            .EnsureStatusAsync(HttpStatusCode.OK);

        using var again = factory.CreateTestClient();
        await SignInWithPasswordAsync(again, username, HttpStatusCode.Accepted);
        await (await again.PostAsync("/api/admin-auth/login/two-factor", new { recoveryCode = recoveryCodes[0] }))
            .EnsureStatusAsync(HttpStatusCode.Unauthorized);

        var status = await client.GetJsonAsync<System.Text.Json.JsonElement>("/api/admin/security/two-factor");
        Assert.Equal(TwoFactorService.RecoveryCodeCount - 1, status.GetProperty("recoveryCodesRemaining").GetInt32());

        await (await client.PostAsync("/api/admin/security/two-factor/disable", new { code = "000000" })).EnsureStatusAsync(HttpStatusCode.BadRequest);
        factory.Time.Advance(TimeSpan.FromSeconds(30));
        await (await client.PostAsync("/api/admin/security/two-factor/disable", new { code = CodeFor(secret) })).EnsureStatusAsync(HttpStatusCode.NoContent);

        using var plain = factory.CreateTestClient();
        await SignInWithPasswordAsync(plain, username, HttpStatusCode.OK);
        var adminId = await factory.WithDbAsync(db => db.Administrators.Where(a => a.Username == username).Select(a => a.Id).SingleAsync());
        Assert.Equal(0, await factory.WithDbAsync(db => db.AdminRecoveryCodes.CountAsync(c => c.AdministratorId == adminId)));
    }

    [Fact]
    public async Task Maintenance_reset_turns_two_factor_off()
    {
        var username = await CreateAdminAsync();
        using var admin = factory.CreateTestClient();
        await SignInWithPasswordAsync(admin, username, HttpStatusCode.OK);
        await EnableAsync(admin);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            Assert.True(await scope.ServiceProvider.GetRequiredService<AdministratorAccounts>()
                .ResetTwoFactorAsync(username, scope.ServiceProvider.GetRequiredService<SessionService>(), CancellationToken.None));
        }

        // Existing sessions were revoked; the password alone works again.
        await (await admin.GetAsync("/api/admin-auth/me")).EnsureStatusAsync(HttpStatusCode.Unauthorized);
        using var client = factory.CreateTestClient();
        await SignInWithPasswordAsync(client, username, HttpStatusCode.OK);
    }
}
