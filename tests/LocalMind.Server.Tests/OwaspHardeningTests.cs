using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LocalMind.Core.Backup.Crypto;
using LocalMind.Core.Backup.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace LocalMind.Server.Tests;

/// <summary>
/// Дрібні виправлення за результатами ручного тестування OWASP API Top 10 (SEC-05).
/// Прибирання заголовка <c>Server</c> тут не перевіряється: TestServer його не додає, а Kestrel — так;
/// це перевірено вручну <c>curl</c>-ом (docs/security-testing.md, F-2).
/// </summary>
public sealed class OwaspHardeningTests(LocalMindServerFactory factory) : IClassFixture<LocalMindServerFactory>
{
    private const string Password = "довгий пароль для тесту";

    [Theory]
    [InlineData("/api/v1/health")]
    [InlineData("/api/v1/auth/me")]
    [InlineData("/api/v1/backups")]
    [InlineData("/api/v1/no-such-endpoint")]
    public async Task Responses_HaveNoSniffAndNoStore(string path)
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task Login_Response_WithTokens_IsNotCacheable()
    {
        using var client = factory.CreateClient();
        var email = $"user-{Guid.NewGuid():N}@example.com";
        using var register = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, Password));

        using var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, Password));

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.True(login.Headers.CacheControl?.NoStore);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(EncryptedEnvelope.HeaderSize + EncryptedEnvelope.TagSize - 1)]
    public async Task Upload_SmallerThanMinimalEnvelope_Returns400AndStoresNothing(int size)
    {
        using var client = await AuthorizedClientAsync();

        using var response = await PostBackupAsync(client, size);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await client.GetFromJsonAsync<BackupInfo[]>(new Uri("/api/v1/backups", UriKind.Relative)) ?? []);
    }

    [Fact]
    public async Task Upload_MinimalEnvelopeSize_IsAccepted()
    {
        using var client = await AuthorizedClientAsync();

        using var response = await PostBackupAsync(client, EncryptedEnvelope.HeaderSize + EncryptedEnvelope.TagSize);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Register_InvalidEmail_ReportsEachMessageOnce()
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/v1/auth/register", new RegisterRequest("not-an-email", Password));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        var messages = problem!.Errors["email"];
        Assert.Equal(messages.Length, messages.Distinct().Count());
    }

    private static async Task<HttpResponseMessage> PostBackupAsync(HttpClient client, int size)
    {
        using var content = new ByteArrayContent(new byte[size]);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return await client.PostAsync(new Uri("/api/v1/backups", UriKind.Relative), content);
    }

    private async Task<HttpClient> AuthorizedClientAsync()
    {
        var client = factory.CreateClient();
        var email = $"user-{Guid.NewGuid():N}@example.com";
        using var register = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, Password));
        using var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, Password));
        login.EnsureSuccessStatusCode();
        var tokens = await login.Content.ReadFromJsonAsync<TokenResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
        return client;
    }
}
