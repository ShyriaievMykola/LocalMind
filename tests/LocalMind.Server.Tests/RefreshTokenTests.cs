using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LocalMind.Core.Backup.Dtos;
using LocalMind.Server.Auth;
using LocalMind.Server.Data;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LocalMind.Server.Tests;

public sealed class RefreshTokenTests(LocalMindServerFactory factory) : IClassFixture<LocalMindServerFactory>
{
    private const string Password = "довгий пароль для тесту";

    [Fact]
    public async Task Login_ReturnsRefreshToken_StoredOnlyAsHash()
    {
        using var client = factory.CreateClient();

        var tokens = await RegisterAndLoginAsync(client);

        Assert.False(string.IsNullOrEmpty(tokens.RefreshToken));
        Assert.InRange(tokens.RefreshTokenExpiresAt - DateTimeOffset.UtcNow, TimeSpan.FromDays(13.9), TimeSpan.FromDays(14));
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
        var hash = RefreshTokenService.Hash(tokens.RefreshToken);
        Assert.True(await db.RefreshTokens.AnyAsync(t => t.TokenHash == hash));
        Assert.False(await db.RefreshTokens.AnyAsync(t => t.TokenHash == tokens.RefreshToken));
    }

    [Fact]
    public async Task Refresh_Valid_ReturnsNewPairThatWorks()
    {
        using var client = factory.CreateClient();
        var tokens = await RegisterAndLoginAsync(client);

        using var response = await RefreshAsync(client, tokens.RefreshToken);
        var next = await response.Content.ReadFromJsonAsync<TokenResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(tokens.RefreshToken, next!.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, (await MeAsync(client, next.AccessToken)).StatusCode);
    }

    [Fact]
    public async Task Refresh_Rotation_NewTokenWorksOldDoesNot()
    {
        using var client = factory.CreateClient();
        var first = await RegisterAndLoginAsync(client);
        var second = await RefreshOkAsync(client, first.RefreshToken);
        var third = await RefreshOkAsync(client, second.RefreshToken);

        Assert.NotEqual(second.RefreshToken, third.RefreshToken);
        using var old = await RefreshAsync(client, second.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, old.StatusCode);
    }

    [Fact]
    public async Task Refresh_ReusedToken_RevokesWholeSession()
    {
        using var client = factory.CreateClient();
        var stolen = await RegisterAndLoginAsync(client);
        var legit = await RefreshOkAsync(client, stolen.RefreshToken);

        using var reuse = await RefreshAsync(client, stolen.RefreshToken);
        using var afterReuse = await RefreshAsync(client, legit.RefreshToken);

        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, afterReuse.StatusCode);
    }

    [Fact]
    public async Task Refresh_ReuseInOneSession_DoesNotAffectOtherSessions()
    {
        using var client = factory.CreateClient();
        var email = NewEmail();
        var laptop = await RegisterAndLoginAsync(client, email);
        var phone = await LoginAsync(client, email);
        _ = await RefreshOkAsync(client, laptop.RefreshToken);

        using var reuse = await RefreshAsync(client, laptop.RefreshToken);

        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);
        _ = await RefreshOkAsync(client, phone.RefreshToken);
    }

    [Fact]
    public async Task Logout_RevokesSession()
    {
        using var client = factory.CreateClient();
        var tokens = await RegisterAndLoginAsync(client);
        var rotated = await RefreshOkAsync(client, tokens.RefreshToken);

        using var logout = await client.PostAsJsonAsync("/api/v1/auth/logout", new LogoutRequest(rotated.RefreshToken));
        using var refresh = await RefreshAsync(client, rotated.RefreshToken);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task Logout_UnknownToken_Returns204()
    {
        using var client = factory.CreateClient();

        using var logout = await client.PostAsJsonAsync("/api/v1/auth/logout", new LogoutRequest("невідомий"));

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("підроблений-токен")]
    public async Task Refresh_UnknownToken_Returns401(string token)
    {
        using var client = factory.CreateClient();

        using var response = await RefreshAsync(client, token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_ExpiredToken_Returns401()
    {
        var clock = new TestTimeProvider(DateTimeOffset.UtcNow);
        using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(
            services => services.AddSingleton<TimeProvider>(clock)));
        using var client = app.CreateClient();
        var tokens = await RegisterAndLoginAsync(client);

        clock.Advance(TimeSpan.FromDays(15));
        using var response = await RefreshAsync(client, tokens.RefreshToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_ConcurrentWithSameToken_OnlyOneSucceeds()
    {
        using var client = factory.CreateClient();
        var tokens = await RegisterAndLoginAsync(client);

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => RefreshAsync(client, tokens.RefreshToken)));

        Assert.True(responses.Count(r => r.StatusCode == HttpStatusCode.OK) <= 1);
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    private static string NewEmail() => $"user-{Guid.NewGuid():N}@example.com";

    private static async Task<TokenResponse> RegisterAndLoginAsync(HttpClient client, string? email = null)
    {
        email ??= NewEmail();
        using var register = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, Password));
        return await LoginAsync(client, email);
    }

    private static async Task<TokenResponse> LoginAsync(HttpClient client, string email)
    {
        using var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, Password));
        login.EnsureSuccessStatusCode();
        return (await login.Content.ReadFromJsonAsync<TokenResponse>())!;
    }

    private static Task<HttpResponseMessage> RefreshAsync(HttpClient client, string token) =>
        client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(token));

    private static async Task<TokenResponse> RefreshOkAsync(HttpClient client, string token)
    {
        using var response = await RefreshAsync(client, token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
    }

    private static async Task<HttpResponseMessage> MeAsync(HttpClient client, string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/v1/auth/me", UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await client.SendAsync(request);
    }

    private sealed class TestTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }
}
