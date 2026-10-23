using LocalMind.Core.Configuration;
using LocalMind.Search.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace LocalMind.Search;

/// <summary>Реєстрація пошуку в DI-контейнері хоста (App, Cli).</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Реєструє налаштування та сервіси пошуку. Викликається разом з
    /// <see cref="Core.ServiceCollectionExtensions.AddLocalMindCore"/>.
    /// Налаштування перевіряються під час запуску хоста, зокрема — що розмір фрагмента
    /// не перевищує ліміт токенів моделі.
    /// </summary>
    /// <param name="services">Колекція сервісів хоста.</param>
    /// <param name="configuration">Конфігурація хоста.</param>
    /// <returns>Та сама колекція — для ланцюжка викликів.</returns>
    public static IServiceCollection AddLocalMindSearch(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<SearchOptions>()
            .Bind(configuration.GetSection(SearchOptions.SectionName))
            .ValidateOnStart();
        services.AddOptions<EmbeddingModelOptions>()
            .Bind(configuration.GetSection(EmbeddingModelOptions.SectionName))
            .ValidateOnStart();

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<SearchOptions>, SearchOptionsValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<EmbeddingModelOptions>, EmbeddingModelOptionsValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<ChunkingOptions>, ChunkSizeWithinModelLimitValidator>());

        return services;
    }
}
