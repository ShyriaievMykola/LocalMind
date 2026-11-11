using System.Net;
using System.Net.Http.Json;
using LocalMind.Core.Backup.Dtos;
using Microsoft.AspNetCore.Mvc;

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
    public async Task Register_DuplicateEmail_Returns409()
    {
        using var client = factory.CreateClient();
        var email = NewEmail();
        using var first = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, StrongPassword));

        using var second = await client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequest(email.ToUpperInvariant(), StrongPassword + "!"));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
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

    private static string NewEmail() => $"user-{Guid.NewGuid():N}@example.com";

    /// <summary>Тіло ProblemDetails без traceId, який різниться між запитами.</summary>
    private static async Task<string> Body(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        return $"{problem!.Status}|{problem.Title}|{problem.Detail}|{problem.Type}";
    }
}
