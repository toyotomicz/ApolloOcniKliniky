using System.Net;
using ApolloData.Api.Tests.Infrastructure;

namespace ApolloData.Api.Tests;

/// <summary>Factory with production-like (low) per-IP limits; runs in its own database container.</summary>
public sealed class LowLimitApiFactory : ApiFactory
{
    protected override IDictionary<string, string?> ExtraSettings => new Dictionary<string, string?>
    {
        ["RateLimiting:OtpRequestPermits"] = "3",
        ["RateLimiting:AdminLoginPermits"] = "3",
        ["RateLimiting:WindowSeconds"] = "600",
    };
}

public sealed class RateLimitTests(LowLimitApiFactory factory) : IClassFixture<LowLimitApiFactory>
{
    [Fact]
    public async Task Code_requests_are_rate_limited_per_client()
    {
        using var client = factory.CreateTestClient();
        for (var i = 0; i < 3; i++)
        {
            await (await client.PostAsync("/api/clinic-auth/request-code", new { email = Scenario.UniqueEmail() }))
                .EnsureStatusAsync(HttpStatusCode.Accepted);
        }

        var limited = await client.PostAsync("/api/clinic-auth/request-code", new { email = Scenario.UniqueEmail() });
        await limited.EnsureStatusAsync(HttpStatusCode.TooManyRequests);
        Assert.Equal("application/problem+json", limited.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Admin_login_attempts_are_rate_limited()
    {
        using var client = factory.CreateTestClient();
        for (var i = 0; i < 3; i++)
        {
            await client.PostAsync("/api/admin-auth/login", new { username = "x", password = "wrong-password" });
        }

        var limited = await client.PostAsync("/api/admin-auth/login", new { username = ApiFactory.AdminUsername, password = ApiFactory.AdminPassword });
        await limited.EnsureStatusAsync(HttpStatusCode.TooManyRequests);
    }
}
