using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using LocalMind.Core.Backup.Dtos;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace LocalMind.Server.Tests;

public sealed class JwtAuthTests(LocalMindServerFactory factory) : IClassFixture<LocalMindServerFactory>
{
    private const string Password = "довгий пароль для тесту";
    private static readonly Uri Me = new("/api/v1/auth/me", UriKind.Relative);

    [Fact]
    public async Task Me_WithoutToken_Returns401()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(Me);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_ValidToken_Returns200()
    {
        using var client = factory.CreateClient();
        var (email, tokens) = await RegisterAndLoginAsync(client);

        using var response = await SendAsync(client, tokens.AccessToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await response.Content.ReadFromJsonAsync<UserInfo>();
        Assert.Equal(email, me!.Email);
        Assert.InRange(tokens.AccessTokenExpiresAt - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(15));
    }

    [Fact]
    public async Task Me_ExpiredToken_Returns401()
    {
        using var client = factory.CreateClient();
        var token = CreateToken(LocalMindServerFactory.SigningKey, expires: DateTime.UtcNow.AddMinutes(-2));

        using var response = await SendAsync(client, token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_ForgedSignature_Returns401()
    {
        using var client = factory.CreateClient();
        var attackerKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        var token = CreateToken(attackerKey, DateTime.UtcNow.AddMinutes(5));

        using var response = await SendAsync(client, token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_TamperedPayload_Returns401()
    {
        using var client = factory.CreateClient();
        var (_, tokens) = await RegisterAndLoginAsync(client);
        var parts = tokens.AccessToken.Split('.');
        var payload = Base64UrlEncoder.Decode(parts[1]).Replace("\"sub\":\"", "\"sub\":\"x", StringComparison.Ordinal);
        var tampered = $"{parts[0]}.{Base64UrlEncoder.Encode(payload)}.{parts[2]}";

        using var response = await SendAsync(client, tampered);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("чужий-issuer", "LocalMind")]
    [InlineData("LocalMind.Server", "чужа-audience")]
    public async Task Me_WrongIssuerOrAudience_Returns401(string issuer, string audience)
    {
        using var client = factory.CreateClient();
        var token = CreateToken(LocalMindServerFactory.SigningKey, DateTime.UtcNow.AddMinutes(5), issuer, audience);

        using var response = await SendAsync(client, token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_UnsignedToken_Returns401()
    {
        using var client = factory.CreateClient();
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "LocalMind.Server",
            Audience = "LocalMind",
            Expires = DateTime.UtcNow.AddMinutes(5),
            Subject = new ClaimsIdentity([new Claim("sub", "будь-хто")]),
        });

        using var response = await SendAsync(client, token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("не base64 — пароль замість ключа, довший за 32 символи")]
    [InlineData("c2hvcnQta2V5LTE2LWJ5dGVz")]
    public void Startup_WithoutValidSigningKey_Fails(string key)
    {
        using var noKey = factory.WithWebHostBuilder(b => b.UseSetting("Jwt:SigningKey", key));

        var error = Assert.Throws<OptionsValidationException>(() => noKey.CreateClient());
        Assert.Contains("Jwt:SigningKey", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Production", false)]
    [InlineData("Development", true)]
    public async Task Me_ExpiredToken_ErrorDetailsOnlyInDevelopment(string environment, bool expected)
    {
        using var client = factory.WithWebHostBuilder(b => b.UseEnvironment(environment)).CreateClient();
        var token = CreateToken(LocalMindServerFactory.SigningKey, expires: DateTime.UtcNow.AddMinutes(-2));

        using var response = await SendAsync(client, token);

        var challenge = string.Join(' ', response.Headers.WwwAuthenticate.Select(h => h.ToString()));
        Assert.StartsWith("Bearer", challenge, StringComparison.Ordinal);
        Assert.Equal(expected, challenge.Contains("error_description", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Me_ValidTokenForDeletedUser_Returns401WithBearerChallenge()
    {
        using var client = factory.CreateClient();
        var token = CreateToken(LocalMindServerFactory.SigningKey, DateTime.UtcNow.AddMinutes(5));

        using var response = await SendAsync(client, token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, h => h.Scheme == "Bearer");
    }

    private static async Task<(string Email, TokenResponse Tokens)> RegisterAndLoginAsync(HttpClient client)
    {
        var email = $"user-{Guid.NewGuid():N}@example.com";
        using var register = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, Password));
        using var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, Password));
        login.EnsureSuccessStatusCode();
        return (email, (await login.Content.ReadFromJsonAsync<TokenResponse>())!);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Me);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    private static string CreateToken(string key, DateTime expires,
        string issuer = "LocalMind.Server", string audience = "LocalMind") =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            NotBefore = expires.AddMinutes(-10),
            IssuedAt = expires.AddMinutes(-10),
            Expires = expires,
            Subject = new ClaimsIdentity([new Claim("sub", "будь-хто")]),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Convert.FromBase64String(key)), SecurityAlgorithms.HmacSha256),
        });
}
