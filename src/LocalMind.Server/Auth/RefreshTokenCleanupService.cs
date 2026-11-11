using LocalMind.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace LocalMind.Server.Auth;

/// <summary>Раз на кілька годин видаляє прострочені refresh-токени, щоб таблиця не росла безмежно.</summary>
public sealed partial class RefreshTokenCleanupService(IServiceScopeFactory scopes, TimeProvider time, ILogger<RefreshTokenCleanupService> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    /// <summary>Видаляє токени, строк яких сплив понад добу тому. Повертає кількість видалених.</summary>
    public async Task<int> CleanupAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
        var threshold = time.GetUtcNow().AddDays(-1);

        // EF Core не перекладає порівняння DateTimeOffset для SQLite. Дати зберігаються текстом ISO 8601 і завжди
        // в UTC (+00:00), тож рядкове порівняння в SQL дає правильний порядок.
        return await db.Database.ExecuteSqlAsync(
            $"DELETE FROM \"RefreshTokens\" WHERE \"ExpiresAt\" < {threshold}", cancellationToken);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var deleted = await CleanupAsync(stoppingToken);
                LogCleaned(logger, deleted);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogCleanupFailed(logger, ex);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Видалено прострочених refresh-токенів: {Count}")]
    private static partial void LogCleaned(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Не вдалося очистити прострочені refresh-токени")]
    private static partial void LogCleanupFailed(ILogger logger, Exception exception);
}
