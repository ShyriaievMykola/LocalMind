using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LocalMind.Core.Storage;

/// <summary>Підготовка сховища під час запуску хоста.</summary>
public static class StorageServiceProviderExtensions
{
    /// <summary>Створює базу, якщо її ще немає, і застосовує нові міграції.</summary>
    public static async Task InitializeLocalMindStorageAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        var factory = services.GetRequiredService<IDbContextFactory<LocalMindDbContext>>();
        await using var context = await factory.CreateDbContextAsync(cancellationToken);

        var folder = Path.GetDirectoryName(context.Database.GetDbConnection().DataSource);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        await context.Database.MigrateAsync(cancellationToken);
    }
}
