using LocalMind.Core.Abstractions;
using LocalMind.Core.Configuration;
using LocalMind.Core.Ingestion;
using LocalMind.Core.Ingestion.Chunking;
using LocalMind.Core.Ingestion.Parsers;
using LocalMind.Core.Ingestion.Watching;
using LocalMind.Core.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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
        services.TryAddSingleton<IChunker>(provider =>
            provider.GetRequiredService<IOptions<ChunkingOptions>>().Value.Strategy == ChunkingStrategy.Paragraph
                ? ActivatorUtilities.CreateInstance<ParagraphChunker>(provider)
                : ActivatorUtilities.CreateInstance<FixedSizeChunker>(provider));
        services.TryAddSingleton<FolderIndexer>();
        services.TryAddSingleton<ChangeDetector>();
        services.TryAddSingleton<FolderWatcher>();
        services.TryAddSingleton<IIndexingService, IndexingService>();

        services.AddOptions<StorageOptions>().Bind(configuration.GetSection(StorageOptions.SectionName));
        services.AddDbContextFactory<LocalMindDbContext>((provider, builder) =>
        {
            var databasePath = provider.GetRequiredService<IOptions<StorageOptions>>().Value.DatabasePath;
            var connection = new SqliteConnectionStringBuilder
            {
                DataSource = string.IsNullOrWhiteSpace(databasePath) ? StorageOptions.DefaultDatabasePath() : databasePath,
                ForeignKeys = true,
            };
            builder.UseSqlite(connection.ToString());
        });
        services.TryAddSingleton<IDocumentRepository, DocumentRepository>();

        return services;
    }
}
