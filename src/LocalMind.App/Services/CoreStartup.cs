using LocalMind.Core.Abstractions;
using LocalMind.Core.Storage;
using LocalMind.Search.Configuration;
using LocalMind.Search.Hybrid;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LocalMind.App.Services;

/// <summary>
/// Запуск ядра у фоні, поки відкривається вікно: міграції бази, завантаження векторів у індекс,
/// перевірка відстежуваних папок. Помилка не валить застосунок — вона потрапляє в <see cref="CoreStatus"/>.
/// </summary>
internal sealed partial class CoreStartup(
    IServiceProvider services,
    SearchService search,
    IIndexingService indexing,
    IOptions<EmbeddingModelOptions> modelOptions,
    CoreStatus status,
    ILogger<CoreStartup> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await services.InitializeLocalMindStorageAsync(stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogStorageFailed(logger, exception);
            status.SetFailed($"База даних недоступна: {exception.Message}");
            return;
        }

        try
        {
            var model = modelOptions.Value;
            var missing = new[] { model.ModelPath, model.TokenizerPath }.FirstOrDefault(path => !File.Exists(path));
            status.SetModel(
                missing is null,
                missing is null ? null : $"Файл моделі не знайдено: {missing}. Запустіть scripts/download-models.");

            await search.LoadVectorsAsync(stoppingToken);

            // Без моделі кожен файл отримав би статус «помилка», тому перевірку папок відкладаємо до її появи.
            if (missing is null)
            {
                await indexing.RescanAsync(stoppingToken);
            }

            status.SetReady();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogStartupFailed(logger, exception);
            status.SetFailed($"Не вдалося запустити пошук: {exception.Message}");
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Не вдалося підготувати базу даних")]
    private static partial void LogStorageFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Не вдалося запустити ядро")]
    private static partial void LogStartupFailed(ILogger logger, Exception exception);
}
