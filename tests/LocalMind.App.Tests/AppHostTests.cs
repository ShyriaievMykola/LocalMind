using LocalMind.App.Services;
using LocalMind.App.ViewModels;
using LocalMind.Core.Abstractions;
using LocalMind.Core.Configuration;
using LocalMind.Search.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LocalMind.App.Tests;

public sealed class AppHostTests : IDisposable
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(30);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public AppHostTests() => Directory.CreateDirectory(_folder);

    [Fact]
    public void Build_ValidatesContainer_AndResolvesServicesForViewModels()
    {
        using var host = BuildHost(Path.Combine(_folder, "app.db"));

        Assert.NotNull(host.Services.GetRequiredService<ISearchService>());
        Assert.NotNull(host.Services.GetRequiredService<IIndexingService>());
        Assert.NotNull(host.Services.GetRequiredService<IDocumentRepository>());
        Assert.NotNull(host.Services.GetRequiredService<MainViewModel>());
    }

    [Fact]
    public async Task Start_WithoutModel_IsReadyAndReportsMissingModel()
    {
        var databasePath = Path.Combine(_folder, "nested", "app.db");
        using var host = BuildHost(databasePath);

        await host.StartAsync();
        var status = host.Services.GetRequiredService<CoreStatus>();
        await status.Completion.WaitAsync(StartupTimeout);
        await host.StopAsync();

        Assert.Equal(CoreStage.Ready, status.Stage);
        Assert.False(status.IsModelAvailable);
        Assert.Contains("missing.onnx", status.ModelError, StringComparison.Ordinal);
        Assert.True(File.Exists(databasePath));
    }

    [Fact]
    public async Task Start_DatabaseUnavailable_ReportsFailureWithoutThrowing()
    {
        // Файл на місці папки — створити базу неможливо на будь-якій ОС.
        var blocker = Path.Combine(_folder, "blocker");
        await File.WriteAllTextAsync(blocker, string.Empty);
        using var host = BuildHost(Path.Combine(blocker, "app.db"));

        await host.StartAsync();
        var status = host.Services.GetRequiredService<CoreStatus>();
        await status.Completion.WaitAsync(StartupTimeout);
        await host.StopAsync();

        Assert.Equal(CoreStage.Failed, status.Stage);
        Assert.NotNull(status.Error);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_folder, recursive: true);
    }

    private static IHost BuildHost(string databasePath)
    {
        var builder = AppHost.CreateBuilder([]);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{StorageOptions.SectionName}:{nameof(StorageOptions.DatabasePath)}"] = databasePath,
            [$"{EmbeddingModelOptions.SectionName}:{nameof(EmbeddingModelOptions.ModelPath)}"] = "no-such-folder/missing.onnx",
        });

        return builder.Build();
    }
}
