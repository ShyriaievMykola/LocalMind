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
        services.AddOptions<ModelCatalogOptions>()
            .Bind(configuration.GetSection(ModelCatalogOptions.SectionName))
            .ValidateOnStart();
        services.AddOptions<EmbeddingModelOptions>()
            .Bind(configuration.GetSection(EmbeddingModelOptions.SectionName))
            .PostConfigure<IOptions<ModelCatalogOptions>>(ApplyCatalog)
            .ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<ModelCatalogOptions>, ModelCatalogOptionsValidator>());

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<SearchOptions>, SearchOptionsValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<EmbeddingModelOptions>, EmbeddingModelOptionsValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<ChunkingOptions>, ChunkSizeWithinModelLimitValidator>());

        services.TryAddSingleton<ModelTokenizer>();
        services.TryAddSingleton<ITokenCounter, HfTokenCounter>();
        services.TryAddSingleton<IEmbedder, OnnxEmbedder>();
        services.TryAddSingleton<IVectorIndex>(provider =>
            new BruteForceIndex(provider.GetRequiredService<IOptions<EmbeddingModelOptions>>().Value.Dimensions));
        services.TryAddSingleton<ISearchService, SearchService>();

        return services;
    }

    /// <summary>
    /// Якщо вказано лише назву моделі (без <c>ModelPath</c>), решту налаштувань беремо з каталогу.
    /// Налаштування з явним <c>ModelPath</c> — власна модель користувача — не змінюються.
    /// </summary>
    private static void ApplyCatalog(EmbeddingModelOptions options, IOptions<ModelCatalogOptions> catalog)
    {
        if (!string.IsNullOrWhiteSpace(options.ModelPath))
        {
            return;
        }

        var model = catalog.Value.Find(options.ModelName);
        if (model is null)
        {
            return;
        }

        options.ModelName = model.Name;
        options.ModelPath = model.LocalModelPath;
        options.TokenizerPath = model.LocalTokenizerPath;
        options.ModelSha256 = model.ModelSha256;
        options.TokenizerSha256 = model.TokenizerSha256;
        options.Dimensions = model.Dimensions;
        options.MaxTokens = model.MaxTokens;
        options.QueryPrefix = model.QueryPrefix;
        options.PassagePrefix = model.PassagePrefix;
    }
}
