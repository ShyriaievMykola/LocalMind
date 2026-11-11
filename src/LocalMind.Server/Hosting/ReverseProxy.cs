using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using IPNetwork = System.Net.IPNetwork;

namespace LocalMind.Server.Hosting;

/// <summary>
/// Reverse proxy перед сервером (секція <c>ReverseProxy</c>, DR-033). Заголовки <c>X-Forwarded-*</c> довіряються
/// лише від перелічених адрес: інакше будь-хто міг би підробити IP і обійти rate limiting.
/// </summary>
public sealed class ReverseProxyOptions
{
    public const string SectionName = "ReverseProxy";

    /// <summary>IP-адреси проксі, наприклад <c>10.0.0.5</c>.</summary>
    public string[] KnownProxies { get; set; } = [];

    /// <summary>Мережі проксі в CIDR, наприклад <c>172.16.0.0/12</c> (мережа Docker).</summary>
    public string[] KnownNetworks { get; set; } = [];

    /// <summary>Чи налаштовано хоч одного довіреного проксі.</summary>
    public bool IsEnabled => KnownProxies.Length > 0 || KnownNetworks.Length > 0;

    /// <summary>Розбирає адреси; невалідне значення — помилка, бо мовчки ігнорований проксі дав би хибні IP клієнтів.</summary>
    internal (IPAddress[] Proxies, IPNetwork[] Networks) Parse()
    {
        var proxies = KnownProxies.Select(p => IPAddress.TryParse(p, out var ip)
            ? ip
            : throw new FormatException($"ReverseProxy:KnownProxies: «{p}» не є IP-адресою.")).ToArray();
        var networks = KnownNetworks.Select(n => IPNetwork.TryParse(n, out var network)
            ? network
            : throw new FormatException($"ReverseProxy:KnownNetworks: «{n}» не є мережею в форматі CIDR.")).ToArray();
        return (proxies, networks);
    }
}

public static class ReverseProxyExtensions
{
    /// <summary>
    /// Реєструє налаштування й, якщо задано довірені проксі, обробку <c>X-Forwarded-For/Proto</c>.
    /// Без налаштування заголовки ігноруються (як у ASP.NET за замовчуванням, окрім loopback).
    /// </summary>
    public static IServiceCollection AddReverseProxy(this IServiceCollection services)
    {
        services.AddOptions<ReverseProxyOptions>()
            .BindConfiguration(ReverseProxyOptions.SectionName)
            .Validate(o =>
            {
                try
                {
                    _ = o.Parse();
                    return true;
                }
                catch (FormatException)
                {
                    return false;
                }
            }, "ReverseProxy: некоректна IP-адреса або мережа CIDR.")
            .ValidateOnStart();

        services.AddOptions<ForwardedHeadersOptions>()
            .Configure<IOptions<ReverseProxyOptions>>((forwarded, proxy) =>
            {
                forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

                // Один проксі перед сервером: беремо лише останній запис, решту (підроблену клієнтом) ігноруємо.
                forwarded.ForwardLimit = 1;

                // За замовчуванням довіряється loopback; замінюємо його явним списком.
                forwarded.KnownProxies.Clear();
                forwarded.KnownIPNetworks.Clear();
                var (proxies, networks) = proxy.Value.Parse();
                foreach (var ip in proxies)
                {
                    forwarded.KnownProxies.Add(ip);
                }

                foreach (var network in networks)
                {
                    forwarded.KnownIPNetworks.Add(network);
                }
            });
        return services;
    }

    /// <summary>Має йти першим у конвеєрі: HSTS, перенаправлення на HTTPS і rate limiting читають схему та IP клієнта.</summary>
    public static WebApplication UseReverseProxy(this WebApplication app)
    {
        if (app.Services.GetRequiredService<IOptions<ReverseProxyOptions>>().Value.IsEnabled)
        {
            app.UseForwardedHeaders();
        }

        return app;
    }
}
