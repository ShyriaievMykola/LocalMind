using LocalMind.Core.Abstractions;
using LocalMind.Core.Configuration;
using LocalMind.Core.Ingestion.Parsers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace LocalMind.Core;

/// <summary>Реєстрація ядра в DI-контейнері хоста (App, Cli, Server).</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Реєструє налаштування та сервіси ядра. Налаштування перевіряються під час запуску хоста:
    /// некоректна конфігурація зупиняє запуск з переліком помилок.
    /// </summary>
    /// <param name="services">Колекція сервісів хоста.</param>
    /// <param name="configuration">Конфігурація хоста.</param>
    /// <returns>Та сама колекція — для ланцюжка викликів.</returns>
    public static IServiceCollection AddLocalMindCore(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<ChunkingOptions>()
            .Bind(configuration.GetSection(ChunkingOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<ChunkingOptions>, ChunkingOptionsValidator>());

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDocumentParser, PlainTextParser>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDocumentParser, MarkdownParser>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDocumentParser, PdfParser>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDocumentParser, DocxParser>());
        services.TryAddSingleton<ParserRegistry>();

        return services;
    }
}
