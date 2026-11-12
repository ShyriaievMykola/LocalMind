using LocalMind.Core.Abstractions;
using LocalMind.Core.Configuration;
using LocalMind.Search.Configuration;
using LocalMind.Search.Embeddings;
using LocalMind.Search.Hybrid;
using LocalMind.Search.Vectors;
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
        services.AddOptions<VectorIndexOptions>()
            .Bind(configuration.GetSection(VectorIndexOptions.SectionName))
            .ValidateOnStart();
        services.AddOptions<EmbeddingModelOptions>()
            .Bind(configuration.GetSection(EmbeddingModelOptions.SectionName))
            .ValidateOnStart();

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<SearchOptions>, SearchOptionsValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<VectorIndexOptions>, VectorIndexOptionsValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<EmbeddingModelOptions>, EmbeddingModelOptionsValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<ChunkingOptions>, ChunkSizeWithinModelLimitValidator>());

        services.TryAddSingleton<ModelTokenizer>();
        services.TryAddSingleton<ITokenCounter, HfTokenCounter>();
        services.TryAddSingleton<IEmbedder, OnnxEmbedder>();
        services.TryAddSingleton<IVectorIndex>(CreateVectorIndex);
        services.TryAddSingleton<ISearchService, SearchService>();

        return services;
    }

    private static IVectorIndex CreateVectorIndex(IServiceProvider provider)
    {
        var dimensions = provider.GetRequiredService<IOptions<EmbeddingModelOptions>>().Value.Dimensions;
        var options = provider.GetRequiredService<IOptions<VectorIndexOptions>>().Value;
        return options.Kind == VectorIndexKind.Hnsw
            ? new HnswIndex(dimensions, options)
            : new BruteForceIndex(dimensions);
    }
}
