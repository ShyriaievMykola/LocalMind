using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LocalMind.Core.Backup.Dtos;
using LocalMind.Server.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LocalMind.Server.Tests.Security;

/// <summary>
/// Сценарії безпеки сервера за <c>docs/threat-model.md</c> (SV-10): чорна скринька, лише через HTTP.
/// Кожен тест має <c>[Trait("Threat", "S-NN")]</c> — ID загрози з документа; <see cref="ThreatModelCoverageTests"/>
/// стежить, щоб ці ID існували в моделі. Тести, що вже є в інших класах, тут не дублюються: модель посилається
/// на них напряму.
/// </summary>
public sealed class ServerThreatTests(LocalMindServerFactory factory) : IClassFixture<LocalMindServerFactory>
{
    private const string Password = "довгий пароль для тесту";
    private static readonly Uri Backups = new("/api/v1/backups", UriKind.Relative);

    // ---------- S-01: підбір пароля ----------

    [Fact]
    [Trait("Threat", "S-01")]
    public async Task Register_PasswordLengthBoundary_ElevenRejectedTwelveAccepted()
    {
        using var client = factory.CreateClient();

        using var eleven = await RegisterAsync(client, NewEmail(), "пароль-для-");
        using var twelve = await RegisterAsync(client, NewEmail(), "пароль-для-т");

        Assert.Equal(HttpStatusCode.BadRequest, eleven.StatusCode);
        Assert.Equal(HttpStatusCode.Created, twelve.StatusCode);
    }

    // ---------- S-04, S-06: токени одного виду не підходять для іншого ----------

    [Fact]
    [Trait("Threat", "S-06")]
    public async Task RefreshToken_UsedAsAccessToken_Returns401()
    {
        using var client = factory.CreateClient();
        var tokens = await RegisterAndLoginAsync(client);

        using var response = await GetAsync(client, "/api/v1/auth/me", tokens.RefreshToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    [Trait("Threat", "S-06")]
    public async Task AccessToken_UsedAsRefreshToken_Returns401AndKeepsSession()
    {
        using var client = factory.CreateClient();
        var tokens = await RegisterAndLoginAsync(client);

        using var wrong = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(tokens.AccessToken));
        using var stillValid = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(tokens.RefreshToken));

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.OK, stillValid.StatusCode);
    }

    // ---------- S-08 / R-02: залишковий ризик зафіксовано тестом ----------

    [Fact]
    [Trait("Threat", "R-02")]
    public async Task AccessToken_AfterLogout_StaysValidUntilExpiry()
    {
        // Це свідомий залишковий ризик (DR-018): якщо тест почне падати, модель загроз треба оновити.
        using var client = factory.CreateClient();
        var tokens = await RegisterAndLoginAsync(client);
        using var logout = await client.PostAsJsonAsync("/api/v1/auth/logout", new LogoutRequest(tokens.RefreshToken));

        using var me = await GetAsync(client, "/api/v1/auth/me", tokens.AccessToken);
        using var refresh = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(tokens.RefreshToken));

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    // ---------- S-10: чужі бекапи ----------

    [Fact]
    [Trait("Threat", "S-10")]
    public async Task Backups_WithoutToken_AllOperationsReturn401()
    {
        using var client = factory.CreateClient();
        var id = Guid.NewGuid();
        using var upload = await client.PostAsync(Backups, new ByteArrayContent(new byte[64]));
        using var list = await client.GetAsync(Backups);
        using var download = await client.GetAsync(new Uri($"/api/v1/backups/{id}", UriKind.Relative));
        using var delete = await client.DeleteAsync(new Uri($"/api/v1/backups/{id}", UriKind.Relative));

        Assert.All(
            new[] { upload, list, download, delete },
            response => Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode));
    }

    [Fact]
    [Trait("Threat", "S-10")]
    public async Task OtherUser_CannotDownloadOrDeleteBackup_AndAnswerEqualsUnknownId()
    {
        using var owner = await AuthorizedClientAsync();
        using var intruder = await AuthorizedClientAsync();
        var info = await UploadAsync(owner, Enumerable.Repeat((byte)7, 64).ToArray());
        var foreign = new Uri($"/api/v1/backups/{info.Id}", UriKind.Relative);
        var unknown = new Uri($"/api/v1/backups/{Guid.NewGuid()}", UriKind.Relative);

        using var getForeign = await intruder.GetAsync(foreign);
        using var getUnknown = await intruder.GetAsync(unknown);
        using var deleteForeign = await intruder.DeleteAsync(foreign);
        using var deleteUnknown = await intruder.DeleteAsync(unknown);
        var intruderList = await intruder.GetFromJsonAsync<BackupInfo[]>(Backups);

        // Чужий бекап не відрізняється від неіснуючого ні кодом, ні тілом: перебір ID нічого не розкриває.
        Assert.Equal(HttpStatusCode.NotFound, getForeign.StatusCode);
        Assert.Equal(getUnknown.StatusCode, getForeign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deleteForeign.StatusCode);
        Assert.Equal(deleteUnknown.StatusCode, deleteForeign.StatusCode);
        Assert.Equal(await WithoutTraceIdAsync(getUnknown), await WithoutTraceIdAsync(getForeign));
        Assert.Empty(intruderList ?? []);

        // Спроба видалення не зачепила бекап власника.
        Assert.Equal(Enumerable.Repeat((byte)7, 64).ToArray(), await owner.GetByteArrayAsync(foreign));
    }

    // ---------- S-11: path traversal ----------

    [Theory]
    [Trait("Threat", "S-11")]
    [InlineData("..%2F..%2Fappsettings.json")]
    [InlineData("%2e%2e%2f%2e%2e%2fappsettings.json")]
    [InlineData("..%5C..%5Cappsettings.json")]
    [InlineData("C%3A%5Cwindows%5Cwin.ini")]
    [InlineData("%2Fetc%2Fpasswd")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task BackupId_TraversalOrGarbage_NeverReturnsFileContent(string id)
    {
        using var client = await AuthorizedClientAsync();
        await UploadAsync(client, new byte[64]);

        using var get = await client.GetAsync(new Uri($"/api/v1/backups/{id}", UriKind.Relative));
        using var delete = await client.DeleteAsync(new Uri($"/api/v1/backups/{id}", UriKind.Relative));
        var body = await get.Content.ReadAsStringAsync();

        Assert.Contains(get.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.BadRequest });
        Assert.Contains(delete.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.BadRequest, HttpStatusCode.MethodNotAllowed });
        Assert.DoesNotContain("ConnectionStrings", body, StringComparison.Ordinal);
        Assert.DoesNotContain("SigningKey", body, StringComparison.Ordinal);
        Assert.Single(await client.GetFromJsonAsync<BackupInfo[]>(Backups) ?? []);
    }

    // ---------- S-13, S-14: блокування й rate limit не зачіпають інших ----------

    [Fact]
    [Trait("Threat", "S-13")]
    public async Task Lockout_OfOneUser_DoesNotAffectAnother()
    {
        using var client = factory.CreateClient();
        var victim = NewEmail();
        using var registerVictim = await RegisterAsync(client, victim, Password);
        var other = await RegisterAndLoginAsync(client);

        for (var i = 0; i < 6; i++)
        {
            using var wrong = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(victim, "неправильний пароль"));
        }

        using var victimWithRightPassword = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(victim, Password));
        using var otherAgain = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(other.RefreshToken));

        Assert.Equal(HttpStatusCode.Unauthorized, victimWithRightPassword.StatusCode);
        Assert.Equal(HttpStatusCode.OK, otherAgain.StatusCode);
    }

    [Fact]
    [Trait("Threat", "S-14")]
    public async Task AuthRateLimit_DoesNotBlockHealthOrOtherEndpoints()
    {
        using var app = factory.WithWebHostBuilder(b => b.UseSetting("RateLimiting:AuthPermitLimit", "1"));
        using var client = app.CreateClient();

        using var first = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(NewEmail(), Password));
        using var limited = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(NewEmail(), Password));
        using var health = await client.GetAsync(new Uri("/api/v1/health", UriKind.Relative));
        using var backups = await client.GetAsync(Backups);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.Contains("Retry-After"));
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, backups.StatusCode);
    }

    // ---------- S-16: помилки без деталей ----------

    [Theory]
    [Trait("Threat", "S-16")]
    [InlineData("/api/v1/auth/register")]
    [InlineData("/api/v1/auth/login")]
    [InlineData("/api/v1/auth/refresh")]
    [InlineData("/api/v1/auth/logout")]
    public async Task MalformedRequests_NeverLeakExceptionDetails(string path)
    {
        using var client = factory.CreateClient();
        var deeplyNested = new string('[', 5000);
        var payloads = new (string Body, string ContentType)[]
        {
            ("{не json", "application/json"),
            ("[]", "application/json"),
            ("null", "application/json"),
            (deeplyNested, "application/json"),
            ("{\"email\": 5, \"password\": []}", "application/json"),
            ("email=a&password=b", "application/x-www-form-urlencoded"),
        };

        foreach (var (body, contentType) in payloads)
        {
            using var content = new StringContent(body, Encoding.UTF8, contentType);
            using var response = await client.PostAsync(new Uri(path, UriKind.Relative), content);
            var text = await response.Content.ReadAsStringAsync();

            Assert.True((int)response.StatusCode < 500, $"{path} ← {body[..Math.Min(body.Length, 20)]}: {(int)response.StatusCode}");
            Assert.DoesNotContain("   at ", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Exception", text, StringComparison.Ordinal);
            Assert.DoesNotContain("LocalMind.Server", text, StringComparison.Ordinal);
        }
    }

    [Theory]
    [Trait("Threat", "S-16")]
    [InlineData("/openapi/v1.json")]
    [InlineData("/swagger")]
    [InlineData("/swagger/index.html")]
    public async Task Production_DoesNotExposeApiDocumentation(string path)
    {
        using var app = factory.WithWebHostBuilder(b => b.UseEnvironment("Production"));
        using var client = app.CreateClient();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- S-17: HSTS у Production ----------

    [Fact]
    [Trait("Threat", "S-17")]
    public async Task Production_OverHttps_SendsHsts()
    {
        using var app = factory.WithWebHostBuilder(b => b.UseEnvironment("Production"));
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://backup.example.com"),
        });

        using var response = await client.GetAsync(new Uri("/api/v1/health", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("Strict-Transport-Security"));
    }

    // ---------- S-18: секрети поза репозиторієм ----------

    [Fact]
    [Trait("Threat", "S-18")]
    public void Repository_HasNoSigningKeyInConfigDockerfileOrCompose()
    {
        var root = ThreatModelCoverageTests.FindRepositoryRoot();
        var server = Path.Combine(root, "src", "LocalMind.Server");

        foreach (var file in Directory.GetFiles(server, "appsettings*.json"))
        {
            using var json = JsonDocument.Parse(File.ReadAllText(file));
            var key = json.RootElement.TryGetProperty("Jwt", out var jwt) && jwt.TryGetProperty("SigningKey", out var value)
                ? value.GetString()
                : null;
            Assert.True(string.IsNullOrEmpty(key), $"{Path.GetFileName(file)} містить Jwt:SigningKey.");
        }

        var dockerfile = File.ReadAllText(Path.Combine(server, "Dockerfile"));
        Assert.DoesNotMatch(@"Jwt__SigningKey\s*=\s*[^\s\\]", dockerfile);

        var gitignore = File.ReadAllText(Path.Combine(root, ".gitignore"));
        Assert.Matches(@"(?m)^\s*\*?\.env\s*$|(?m)^\s*deploy/\.env\s*$", gitignore);
    }

    // ---------- S-19: у БД сервера немає відкритих секретів ----------

    [Fact]
    [Trait("Threat", "S-19")]
    public async Task Database_HoldsOnlyHashesOfPasswordsAndRefreshTokens()
    {
        using var client = factory.CreateClient();
        var email = NewEmail();
        using var register = await RegisterAsync(client, email, Password);
        using var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, Password));
        var tokens = (await login.Content.ReadFromJsonAsync<TokenResponse>())!;

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Email == email);
        var stored = await db.RefreshTokens.AsNoTracking().Where(t => t.UserId == user.Id).ToListAsync();

        Assert.NotNull(user.PasswordHash);
        Assert.DoesNotContain(Password, user.PasswordHash, StringComparison.Ordinal);
        Assert.NotEmpty(stored);
        Assert.All(stored, token =>
        {
            Assert.NotEqual(tokens.RefreshToken, token.TokenHash);
            Assert.DoesNotContain(tokens.RefreshToken, token.TokenHash, StringComparison.Ordinal);
            Assert.Matches("^[0-9a-f]{64}$", token.TokenHash);
        });
    }

    // ---------- допоміжне ----------

    // Відповідь ProblemDetails без traceId, який різний у кожного запиту.
    private static async Task<string> WithoutTraceIdAsync(HttpResponseMessage response)
    {
        var fields = await response.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>() ?? [];
        fields.Remove("traceId");
        return JsonSerializer.Serialize(fields.OrderBy(f => f.Key, StringComparer.Ordinal).ToDictionary(f => f.Key, f => f.Value));
    }

    private static string NewEmail() => $"user-{Guid.NewGuid():N}@example.com";

    private static Task<HttpResponseMessage> RegisterAsync(HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, password));

    private static async Task<TokenResponse> RegisterAndLoginAsync(HttpClient client)
    {
        var email = NewEmail();
        using var register = await RegisterAsync(client, email, Password);
        using var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, Password));
        login.EnsureSuccessStatusCode();
        return (await login.Content.ReadFromJsonAsync<TokenResponse>())!;
    }

    private async Task<HttpClient> AuthorizedClientAsync()
    {
        var client = factory.CreateClient();
        var tokens = await RegisterAndLoginAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return client;
    }

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string path, string bearer)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return await client.SendAsync(request);
    }

    private static async Task<BackupInfo> UploadAsync(HttpClient client, byte[] data)
    {
        using var content = new ByteArrayContent(data);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var response = await client.PostAsync(Backups, content);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<BackupInfo>())!;
    }
}
