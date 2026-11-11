using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LocalMind.Server.Tests;

public sealed class ServerSkeletonTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Secret = "секретна-деталь-винятку";

    [Fact]
    public async Task Health_Returns200()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/health", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task WeatherForecast_Removed()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/weatherforecast", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task UnhandledException_ReturnsProblemDetailsWithoutStackTrace(string environment)
    {
        using var client = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.ConfigureServices(services => services.AddTransient<IStartupFilter, ThrowingEndpointFilter>());
        }).CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/__throw", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain(Secret, body, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(InvalidOperationException), body, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(ThrowingEndpointFilter), body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenApi_AvailableOnlyInDevelopment()
    {
        using var dev = factory.CreateClient();
        using var prod = factory.WithWebHostBuilder(b => b.UseEnvironment("Production")).CreateClient();

        using var devDoc = await dev.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));
        using var devUi = await dev.GetAsync(new Uri("/swagger/index.html", UriKind.Relative));
        using var prodDoc = await prod.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));
        using var prodUi = await prod.GetAsync(new Uri("/swagger/index.html", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, devDoc.StatusCode);
        Assert.Equal(HttpStatusCode.OK, devUi.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, prodDoc.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, prodUi.StatusCode);
    }

    /// <summary>Додає в кінець конвеєра ендпоінт, що кидає виняток (всередині обробника винятків).</summary>
    private sealed class ThrowingEndpointFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Map("/api/v1/__throw", a => a.Run(_ => throw new InvalidOperationException(Secret)));
        };
    }
}
