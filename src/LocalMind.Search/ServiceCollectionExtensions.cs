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
    /// Якщо <c>ModelName</c> є в каталозі, опис моделі (токенізатор, хеші, розмірність, ліміт токенів, префікси) беремо
    /// з каталогу. Шлях лишається, лише якщо його задали свідомо (інша тека): порожній шлях або шлях
    /// стандартної теки ІНШОЇ моделі каталогу вважається незаданим — так збережений у налаштуваннях застосунку шлях
    /// попередньої моделі не вказує на файл не тієї моделі.
    /// Модель, якої немає в каталозі, — власна модель користувача — лишається як налаштовано.
    /// </summary>
    private static void ApplyCatalog(EmbeddingModelOptions options, IOptions<ModelCatalogOptions> catalog) =>
        ApplyCatalog(options, catalog.Value);

    /// <summary>Те саме для вже прочитаного каталогу (його використовує і eval для ефективних налаштувань).</summary>
    internal static void ApplyCatalog(EmbeddingModelOptions options, ModelCatalogOptions models)
    {
        var model = models.Find(options.ModelName);
        if (model is null)
        {
            return;
        }

        var others = models.Models.Where(other => !ReferenceEquals(other, model)).ToList();
        options.ModelName = model.Name;
        if (IsUnset(options.ModelPath, others.Select(other => other.LocalModelPath)))
        {
            options.ModelPath = model.LocalModelPath;
        }

        if (IsUnset(options.TokenizerPath, others.Select(other => other.LocalTokenizerPath)))
        {
            options.TokenizerPath = model.LocalTokenizerPath;
        }

        options.ModelSha256 = model.ModelSha256;
        options.TokenizerSha256 = model.TokenizerSha256;
        options.Dimensions = model.Dimensions;
        options.MaxTokens = model.MaxTokens;
        options.QueryPrefix = model.QueryPrefix;
        options.PassagePrefix = model.PassagePrefix;
    }

    /// <summary>Шлях не заданий: порожній або збігається (в тому числі як хвіст абсолютного шляху) зі стандартним шляхом іншої моделі.</summary>
    private static bool IsUnset(string? path, IEnumerable<string> otherModelsDefaults)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        var normalized = path.Replace('\\', '/');
        return otherModelsDefaults.Any(other =>
            normalized.Equals(other, StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith("/" + other, StringComparison.OrdinalIgnoreCase));
    }
}
