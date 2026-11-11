using System.Net;
using System.Net.Http.Json;
using LocalMind.Core.Backup.Dtos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace LocalMind.Server.Tests;

/// <summary>Сервер за reverse proxy: IP клієнта береться з X-Forwarded-For лише від довіреного проксі (DR-033).</summary>
public sealed class ReverseProxyTests(LocalMindServerFactory factory) : IClassFixture<LocalMindServerFactory>
{
    private static readonly IPAddress ProxyAddress = IPAddress.Parse("172.18.0.2");

    [Fact]
    public async Task TrustedProxy_RateLimitIsPerForwardedClient()
    {
        using var app = Proxied(trusted: true);
        using var client = app.CreateClient();

        using var firstA = await LoginAsync(client, "1.1.1.1");
        using var firstB = await LoginAsync(client, "1.1.1.1");
        using var limited = await LoginAsync(client, "1.1.1.1");
        using var otherClient = await LoginAsync(client, "2.2.2.2");

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, otherClient.StatusCode);
    }

    [Fact]
    public async Task UntrustedProxy_SpoofedForwardedForDoesNotBypassRateLimit()
    {
        using var app = Proxied(trusted: false);
        using var client = app.CreateClient();

        using var firstA = await LoginAsync(client, "1.1.1.1");
        using var firstB = await LoginAsync(client, "2.2.2.2");
        using var limited = await LoginAsync(client, "3.3.3.3");

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [Theory]
    [InlineData("KnownNetworks", "не-мережа")]
    [InlineData("KnownNetworks", "10.0.0.0/99")]
    [InlineData("KnownProxies", "не-ip")]
    public void InvalidProxySettings_StopStartup(string key, string value)
    {
        using var app = factory.WithWebHostBuilder(b => b.UseSetting($"ReverseProxy:{key}:0", value));

        Assert.ThrowsAny<Exception>(() => app.CreateClient());
    }

    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> Proxied(bool trusted) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("RateLimiting:AuthPermitLimit", "2");
            if (trusted)
            {
                builder.UseSetting("ReverseProxy:KnownNetworks:0", "172.18.0.0/16");
            }

            // TestServer не має віддаленої адреси; ставимо адресу проксі до обробки X-Forwarded-*.
            builder.ConfigureServices(services => services.AddTransient<IStartupFilter, FakeProxyStartupFilter>());
        });

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string forwardedFor)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/v1/auth/login", UriKind.Relative))
        {
            Content = JsonContent.Create(new LoginRequest("nobody@example.com", "довгий пароль для тесту")),
        };
        request.Headers.Add("X-Forwarded-For", forwardedFor);
        return await client.SendAsync(request);
    }

    private sealed class FakeProxyStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = ProxyAddress;
                return nextMiddleware();
            });
            next(app);
        };
    }
}
