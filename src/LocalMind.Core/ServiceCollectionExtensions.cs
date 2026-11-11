using LocalMind.Core.Abstractions;
using LocalMind.Core.Backup;
using LocalMind.Core.Configuration;
using LocalMind.Core.Ingestion;
using LocalMind.Core.Ingestion.Chunking;
using LocalMind.Core.Ingestion.Parsers;
using LocalMind.Core.Ingestion.Watching;
using LocalMind.Core.Storage;
using LocalMind.Core.Storage.FullText;
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
        services.TryAddSingleton<IKeywordIndex, Fts5KeywordIndex>();

        return services;
    }

    /// <summary>
    /// Реєструє клієнт бекапу: <see cref="BackupApiClient"/> (typed HttpClient), сховище токенів
    /// (DPAPI на Windows, інакше лише пам'ять), <see cref="BackupService"/> і <see cref="RestoreService"/>.
    /// Потребує <see cref="AddLocalMindCore"/>.
    /// </summary>
    public static IServiceCollection AddLocalMindBackup(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<BackupClientOptions>()
            .Bind(configuration.GetSection(BackupClientOptions.SectionName))
            .Validate(o => BackupClientOptions.IsValidServerUrl(o.ServerUrl),
                "LocalMind:Backup:ServerUrl має бути абсолютною HTTPS-адресою (HTTP — лише для localhost).")
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ITokenStore>(provider =>
        {
            if (!OperatingSystem.IsWindows())
            {
                return new InMemoryTokenStore();
            }

            var path = provider.GetRequiredService<IOptions<BackupClientOptions>>().Value.TokenFilePath;
            return new DpapiTokenStore(string.IsNullOrWhiteSpace(path) ? BackupClientOptions.DefaultTokenFilePath() : path);
        });
        services.AddHttpClient<BackupApiClient>((provider, client) =>
        {
            client.BaseAddress = provider.GetRequiredService<IOptions<BackupClientOptions>>().Value.ServerUrl;
            client.Timeout = TimeSpan.FromMinutes(5);
        });
        services.TryAddTransient<BackupService>();
        services.TryAddTransient<RestoreService>();

        return services;
    }
}
