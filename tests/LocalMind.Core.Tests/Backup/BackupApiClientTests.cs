using System.Net;
using System.Net.Http.Json;
using LocalMind.Core.Backup;
using LocalMind.Core.Backup.Dtos;
using LocalMind.Core.Errors;

namespace LocalMind.Core.Tests.Backup;

public sealed class BackupApiClientTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Login_SavesTokens()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.OK, Tokens("a1", "r1")));
        var (client, store) = Create(handler);

        await client.LoginAsync("user@example.com", "пароль");

        Assert.Equal("a1", store.Load()!.AccessToken);
        Assert.True(client.IsSignedIn);
    }

    [Fact]
    public async Task Request_Unauthorized_RefreshesAndRetries()
    {
        var handler = new FakeHandler(request => (request.RequestUri!.AbsolutePath, Bearer(request)) switch
        {
            ("/api/v1/backups", "old") => new HttpResponseMessage(HttpStatusCode.Unauthorized),
            ("/api/v1/auth/refresh", _) => Json(HttpStatusCode.OK, Tokens("new", "r2")),
            ("/api/v1/backups", "new") => Json(HttpStatusCode.OK, Array.Empty<BackupInfo>()),
            _ => throw new InvalidOperationException($"Неочікуваний запит {request.RequestUri}"),
        });
        var (client, store) = Create(handler, Tokens("old", "r1"));

        var list = await client.ListBackupsAsync();

        Assert.Empty(list);
        Assert.Equal(["/api/v1/backups", "/api/v1/auth/refresh", "/api/v1/backups"], handler.Paths);
        Assert.Equal("r2", store.Load()!.RefreshToken);
        Assert.Equal("r1", handler.RefreshTokensSent.Single());
    }

    [Fact]
    public async Task Refresh_Rejected_SignsOutAndThrowsSessionExpired()
    {
        // Так виглядає повторне використання: сервер відкликав сесію і відповідає 401 на refresh.
        var handler = new FakeHandler(request => request.RequestUri!.AbsolutePath == "/api/v1/auth/refresh"
            ? Json(HttpStatusCode.Unauthorized, new { title = "Сесія недійсна" })
            : new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var (client, store) = Create(handler, Tokens("old", "stolen"));

        var error = await Assert.ThrowsAsync<BackupApiException>(() => client.ListBackupsAsync());

        Assert.True(error.IsSessionExpired);
        Assert.Null(store.Load());
        Assert.False(client.IsSignedIn);
    }

    [Fact]
    public async Task Request_UnauthorizedAfterRefresh_SignsOut()
    {
        var handler = new FakeHandler(request => request.RequestUri!.AbsolutePath == "/api/v1/auth/refresh"
            ? Json(HttpStatusCode.OK, Tokens("new", "r2"))
            : new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var (client, store) = Create(handler, Tokens("old", "r1"));

        var error = await Assert.ThrowsAsync<BackupApiException>(() => client.ListBackupsAsync());

        Assert.True(error.IsSessionExpired);
        Assert.Null(store.Load());
    }

    [Fact]
    public async Task Request_AccessTokenAboutToExpire_RefreshesFirst()
    {
        var handler = new FakeHandler(request => request.RequestUri!.AbsolutePath == "/api/v1/auth/refresh"
            ? Json(HttpStatusCode.OK, Tokens("new", "r2"))
            : Json(HttpStatusCode.OK, Array.Empty<BackupInfo>()));
        var (client, _) = Create(handler, Tokens("old", "r1", accessExpiresAt: Now.AddSeconds(10)));

        await client.ListBackupsAsync();

        Assert.Equal(["/api/v1/auth/refresh", "/api/v1/backups"], handler.Paths);
        Assert.Equal("new", handler.BearersSent.Single());
    }

    [Fact]
    public async Task ConcurrentUnauthorized_RefreshOnlyOnce()
    {
        var handler = new FakeHandler(request => (request.RequestUri!.AbsolutePath, Bearer(request)) switch
        {
            ("/api/v1/auth/refresh", _) => Json(HttpStatusCode.OK, Tokens("new", "r2")),
            (_, "old") => new HttpResponseMessage(HttpStatusCode.Unauthorized),
            _ => Json(HttpStatusCode.OK, Array.Empty<BackupInfo>()),
        });
        var (client, _) = Create(handler, Tokens("old", "r1"));

        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => client.ListBackupsAsync()));

        Assert.Equal(1, handler.Paths.Count(p => p == "/api/v1/auth/refresh"));
    }

    [Fact]
    public async Task NotSignedIn_ThrowsSessionExpiredWithoutRequest()
    {
        var handler = new FakeHandler(_ => throw new InvalidOperationException("Запиту не має бути"));
        var (client, _) = Create(handler);

        var error = await Assert.ThrowsAsync<BackupApiException>(() => client.ListBackupsAsync());

        Assert.True(error.IsSessionExpired);
        Assert.Empty(handler.Paths);
    }

    [Fact]
    public async Task Logout_RevokesOnServerAndClearsTokens()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var (client, store) = Create(handler, Tokens("a", "r1"));

        await client.LogoutAsync();

        Assert.Null(store.Load());
        Assert.Equal(["/api/v1/auth/logout"], handler.Paths);
        Assert.Equal("r1", handler.RefreshTokensSent.Single());
    }

    [Fact]
    public async Task Logout_ServerDown_StillClearsTokens()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("немає мережі"));
        var (client, store) = Create(handler, Tokens("a", "r1"));

        await client.LogoutAsync();

        Assert.Null(store.Load());
    }

    [Fact]
    public async Task Upload_TooLarge_ThrowsWithServerMessage()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.RequestEntityTooLarge, new { title = "Бекап перевищує 100 МБ." }));
        var (client, _) = Create(handler, Tokens("a", "r1"));

        var error = await Assert.ThrowsAsync<BackupApiException>(() => client.UploadBackupAsync(new byte[10]));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, error.StatusCode);
        Assert.Equal("Бекап перевищує 100 МБ.", error.Message);
    }

    [Fact]
    public async Task Register_ValidationErrors_JoinedIntoMessage()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.BadRequest, new
        {
            title = "One or more validation errors occurred.",
            errors = new Dictionary<string, string[]> { ["password"] = ["Пароль має містити щонайменше 12 символів."] },
        }));
        var (client, _) = Create(handler);

        var error = await Assert.ThrowsAsync<BackupApiException>(() => client.RegisterAsync("a@example.com", "короткий"));

        Assert.Equal("Пароль має містити щонайменше 12 символів.", error.Message);
    }

    [Fact]
    public async Task ServerUnavailable_ThrowsFriendlyError()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("connection refused"));
        var (client, _) = Create(handler);

        var error = await Assert.ThrowsAsync<BackupApiException>(() => client.LoginAsync("a@example.com", "пароль"));

        Assert.Null(error.StatusCode);
        Assert.Contains("недоступний", error.Message, StringComparison.Ordinal);
    }

    private static (BackupApiClient Client, InMemoryTokenStore Store) Create(FakeHandler handler, TokenResponse? tokens = null)
    {
        var store = new InMemoryTokenStore();
        if (tokens is not null)
        {
            store.Save(tokens);
        }

        var http = new HttpClient(handler) { BaseAddress = new Uri("https://backup.test/") };
        return (new BackupApiClient(http, store, new FixedTime(Now)), store);
    }

    private static TokenResponse Tokens(string access, string refresh, DateTimeOffset? accessExpiresAt = null) =>
        new(access, accessExpiresAt ?? Now.AddMinutes(15), refresh, Now.AddDays(14));

    private static string? Bearer(HttpRequestMessage request) => request.Headers.Authorization?.Parameter;

    private static HttpResponseMessage Json<T>(HttpStatusCode status, T body) =>
        new(status) { Content = JsonContent.Create(body) };

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        private readonly Lock _lock = new();

        public List<string> Paths { get; } = [];

        public List<string> RefreshTokensSent { get; } = [];

        public List<string> BearersSent { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            string? refresh = null;
            if (path is "/api/v1/auth/refresh" or "/api/v1/auth/logout")
            {
                refresh = (await request.Content!.ReadFromJsonAsync<RefreshRequest>(cancellationToken))!.RefreshToken;
            }

            lock (_lock)
            {
                Paths.Add(path);
                if (refresh is not null)
                {
                    RefreshTokensSent.Add(refresh);
                }

                if (Bearer(request) is { } bearer)
                {
                    BearersSent.Add(bearer);
                }
            }

            // Невелика затримка, щоб паралельні запити справді перетинались.
            await Task.Delay(5, cancellationToken);
            return respond(request);
        }
    }
}
