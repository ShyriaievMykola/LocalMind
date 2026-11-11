using System.Net;
using System.Net.Http.Json;
using System.Text;
using LocalMind.Core.Backup.Dtos;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace LocalMind.Server.Tests;

public sealed class AuthEndpointsTests(LocalMindServerFactory factory) : IClassFixture<LocalMindServerFactory>
{
    private const string StrongPassword = "довгий пароль для тесту";

    [Fact]
    public async Task Register_Valid_Returns201AndAllowsLogin()
    {
        using var client = factory.CreateClient();
        var email = NewEmail();

        using var register = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, StrongPassword));
        using var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, StrongPassword));

        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        Assert.True(login.IsSuccessStatusCode, $"login → {login.StatusCode}");
    }

    [Fact]
    public async Task Register_ExistingEmail_LooksLikeNewRegistrationAndKeepsOldPassword()
    {
        using var client = factory.CreateClient();
        var email = NewEmail();
        using var first = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, StrongPassword));
        using var fresh = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(NewEmail(), StrongPassword));

        using var second = await client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequest(email.ToUpperInvariant(), StrongPassword + "!"));
        using var hijack = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, StrongPassword + "!"));
        using var owner = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, StrongPassword));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(fresh.StatusCode, second.StatusCode);
        Assert.Equal(await fresh.Content.ReadAsStringAsync(), await second.Content.ReadAsStringAsync());
        Assert.Equal(fresh.Headers.Location, second.Headers.Location);
        Assert.Equal(HttpStatusCode.Unauthorized, hijack.StatusCode);
        Assert.True(owner.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Register_ExistingEmailWithWeakPassword_StillReportsPassword()
    {
        using var client = factory.CreateClient();
        var email = NewEmail();
        using var first = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, StrongPassword));

        using var weak = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "короткий"));

        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
    }

    [Theory]
    [InlineData("короткий")]
    [InlineData("aaaaaaaaaaaaaaaa")]
    public async Task Register_WeakPassword_Returns400(string password)
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(NewEmail(), password));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains("password", problem!.Errors.Keys);
    }

    [Theory]
    [InlineData("не-email")]
    [InlineData("")]
    public async Task Register_InvalidEmail_Returns400(string email)
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, StrongPassword));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_WrongPasswordAndUnknownUser_ReturnSameResponse()
    {
        using var client = factory.CreateClient();
        var email = NewEmail();
        using var register = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, StrongPassword));

        using var wrongPassword = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "інший довгий пароль"));
        using var unknownUser = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(NewEmail(), StrongPassword));

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownUser.StatusCode);
        Assert.Equal(await Body(wrongPassword), await Body(unknownUser));
    }

    [Fact]
    public async Task Login_AfterFiveFailures_LockedOutWithSameResponse()
    {
        using var client = factory.CreateClient();
        var email = NewEmail();
        using var register = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, StrongPassword));
        for (var i = 0; i < 5; i++)
        {
            using var failed = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "неправильний пароль"));
        }

        using var locked = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, StrongPassword));
        using var unknown = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(NewEmail(), StrongPassword));

        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
        Assert.Equal(await Body(unknown), await Body(locked));
    }

    [Fact]
    public async Task Login_LockedOut_StillHashesPassword()
    {
        var hasher = new CountingHasher();
        using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(
            services => services.AddSingleton<IPasswordHasher<IdentityUser>>(hasher)));
        using var client = app.CreateClient();
        var email = NewEmail();
        using var register = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, StrongPassword));
        for (var i = 0; i < 5; i++)
        {
            using var failed = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "неправильний пароль"));
        }

        var before = hasher.Verifications;
        using var locked = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, StrongPassword));

        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
        Assert.Equal(before + 1, hasher.Verifications);
    }

    [Theory]
    [InlineData("Development", "null")]
    [InlineData("Development", "")]
    [InlineData("Development", "{ кривий json")]
    [InlineData("Production", "{ кривий json")]
    public async Task Auth_MalformedBody_Returns400(string environment, string body)
    {
        using var client = factory.WithWebHostBuilder(b => b.UseEnvironment(environment)).CreateClient();

        foreach (var path in new[] { "/api/v1/auth/register", "/api/v1/auth/login" })
        {
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(new Uri(path, UriKind.Relative), content);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [Theory]
    [InlineData("a b@example.com")]
    [InlineData("<script>@example.com")]
    [InlineData("Ім'я <user@example.com>")]
    [InlineData("user@localhost")]
    [InlineData("user@example.com")]
    [MemberData(nameof(TooLongEmails))]
    public async Task Register_MalformedEmail_Returns400(string email)
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, StrongPassword));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    public static TheoryData<string> TooLongEmails => new()
    {
        new string('a', 65) + "@example.com",
        "user@" + new string('d', 250) + ".com",
    };

    [Fact]
    public async Task Register_SameEmailInNfcAndNfd_IsOneAccount()
    {
        using var client = factory.CreateClient();
        var nfc = $"й-{Guid.NewGuid():N}@example.com".Normalize(NormalizationForm.FormC);
        var nfd = nfc.Normalize(NormalizationForm.FormD);

        using var first = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(nfc, StrongPassword));
        using var second = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(nfd, StrongPassword + "!"));
        using var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(nfd, StrongPassword));
        using var secondPassword = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(nfc, StrongPassword + "!"));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.True(login.IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, secondPassword.StatusCode);
    }

    [Fact]
    public async Task Register_CyrillicEmail_Returns201()
    {
        using var client = factory.CreateClient();
        var email = $"користувач-{Guid.NewGuid():N}@приклад.укр";

        using var response = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, StrongPassword));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Register_ShortPassword_ReturnsUkrainianMessage()
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(NewEmail(), "короткий"));

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains(problem!.Errors["password"], m => m.Contains("щонайменше 12", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Register_PasswordContainsEmail_Returns400()
    {
        using var client = factory.CreateClient();
        var local = $"longname{Guid.NewGuid():N}";

        using var response = await client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequest($"{local}@example.com", $"{local}@example.com"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains("password", problem!.Errors.Keys);
    }

    private static string NewEmail() => $"user-{Guid.NewGuid():N}@example.com";

    /// <summary>Тіло ProblemDetails без traceId, який різниться між запитами.</summary>
    private static async Task<string> Body(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        return $"{problem!.Status}|{problem.Title}|{problem.Detail}|{problem.Type}";
    }

    private sealed class CountingHasher : IPasswordHasher<IdentityUser>
    {
        private readonly PasswordHasher<IdentityUser> _inner = new();
        private int _verifications;

        public int Verifications => Volatile.Read(ref _verifications);

        public string HashPassword(IdentityUser user, string password) => _inner.HashPassword(user, password);

        public PasswordVerificationResult VerifyHashedPassword(IdentityUser user, string hashedPassword, string providedPassword)
        {
            Interlocked.Increment(ref _verifications);
            return _inner.VerifyHashedPassword(user, hashedPassword, providedPassword);
        }
    }
}
