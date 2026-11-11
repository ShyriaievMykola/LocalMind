using System.Net;
using System.Net.Http.Json;
using LocalMind.Core.Backup.Dtos;
using LocalMind.Server.Auth;
using LocalMind.Server.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LocalMind.Server.Tests;

/// <summary>Захист від перебору й переповнення (SV-09, DR-032).</summary>
public sealed class AbuseProtectionTests(LocalMindServerFactory factory) : IClassFixture<LocalMindServerFactory>
{
    private const string Password = "довгий пароль для тесту";

    [Fact]
    public async Task Auth_TooManyRequestsFromOneIp_Returns429WithRetryAfter()
    {
        using var app = factory.WithWebHostBuilder(b => b.UseSetting("RateLimiting:AuthPermitLimit", "3"));
        using var client = app.CreateClient();
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            using var attempt = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(NewEmail(), Password));
            statuses.Add(attempt.StatusCode);
        }

        using var limited = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(NewEmail(), Password));
        using var otherEndpoint = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(NewEmail(), Password));

        Assert.All(statuses, s => Assert.Equal(HttpStatusCode.Unauthorized, s));
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter);
        var problem = await limited.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(429, problem!.Status);
        Assert.Equal(HttpStatusCode.TooManyRequests, otherEndpoint.StatusCode);
    }

    [Fact]
    public async Task RefreshAndMe_HaveSeparateSofterLimit()
    {
        using var app = factory.WithWebHostBuilder(b => b.UseSetting("RateLimiting:AuthPermitLimit", "2"));
        using var client = app.CreateClient();
        var tokens = await RegisterAndLoginAsync(client);
        using var secondLogin = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(NewEmail(), Password));
        using var loginLimited = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(NewEmail(), Password));

        using var refresh = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(tokens.RefreshToken));

        Assert.Equal(HttpStatusCode.TooManyRequests, loginLimited.StatusCode);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
    }

    [Fact]
    public async Task RateLimit_DoesNotApplyToHealth()
    {
        using var app = factory.WithWebHostBuilder(b => b.UseSetting("RateLimiting:AuthPermitLimit", "1"));
        using var client = app.CreateClient();
        using var first = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(NewEmail(), Password));
        using var second = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(NewEmail(), Password));

        using var health = await client.GetAsync(new Uri("/api/v1/health", UriKind.Relative));

        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Fact]
    public async Task Lockout_IsTemporary()
    {
        using var client = factory.CreateClient();
        var email = NewEmail();
        using var register = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, Password));
        for (var i = 0; i < 5; i++)
        {
            using var failed = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "неправильний пароль"));
        }

        using var locked = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, Password));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var user = (await users.FindByEmailAsync(email))!;
            var end = await users.GetLockoutEndDateAsync(user);
            Assert.NotNull(end);
            Assert.InRange(end.Value - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(15));

            // Імітуємо, що 15 хвилин минули.
            await users.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddSeconds(-1));
        }

        using var afterLockout = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, Password));

        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
        Assert.True(afterLockout.IsSuccessStatusCode, $"після блокування → {afterLockout.StatusCode}");
    }

    [Fact]
    public async Task Cleanup_DeletesOnlyLongExpiredRefreshTokens()
    {
        var clock = new TestTimeProvider(DateTimeOffset.UtcNow);
        using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(
            services => services.AddSingleton<TimeProvider>(clock)));
        using var client = app.CreateClient();
        var email = NewEmail();
        using var register = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, Password));
        var old = await LoginAsync(client, email);

        clock.Advance(TimeSpan.FromDays(16));
        var fresh = await LoginAsync(client, email);
        var deleted = await app.Services.GetRequiredService<RefreshTokenCleanupService>().CleanupAsync();

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
        var hashes = await db.RefreshTokens.Select(t => t.TokenHash).ToListAsync();
        Assert.True(deleted >= 1);
        Assert.DoesNotContain(RefreshTokenService.Hash(old.RefreshToken), hashes);
        Assert.Contains(RefreshTokenService.Hash(fresh.RefreshToken), hashes);
    }

    private static async Task<TokenResponse> RegisterAndLoginAsync(HttpClient client)
    {
        var email = NewEmail();
        using var register = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, Password));
        return await LoginAsync(client, email);
    }

    private static string NewEmail() => $"user-{Guid.NewGuid():N}@example.com";

    private static async Task<TokenResponse> LoginAsync(HttpClient client, string email)
    {
        using var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, Password));
        login.EnsureSuccessStatusCode();
        return (await login.Content.ReadFromJsonAsync<TokenResponse>())!;
    }

    private sealed class TestTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }
}
