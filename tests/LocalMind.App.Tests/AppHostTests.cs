using LocalMind.App.Services;
using LocalMind.App.ViewModels;
using LocalMind.Core.Abstractions;
using LocalMind.Core.Configuration;
using LocalMind.Core.Models;
using LocalMind.Search.Configuration;
using LocalMind.TestUtilities;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace LocalMind.App.Tests;

public sealed class AppHostTests : IDisposable
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(30);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public AppHostTests() => Directory.CreateDirectory(_folder);

    
    /// <summary>Окремий файл налаштувань: тести не читають справжній settings.json користувача.</summary>
    private string SettingsPath => Path.Combine(_folder, "settings.json");

    [Fact]
    public async Task Build_ResultCardCommand_UsesRegisteredFileLauncher()
    {
        var files = new FakeFileLauncher();
        var result = new SearchResult(TestData.Chunk(Guid.NewGuid()), Path.Combine(_folder, "note.md"), 0.9);
        var search = new FakeSearchService { Handler = (_, _) => Task.FromResult<IReadOnlyList<SearchResult>>([result]) };
        var builder = AppHost.CreateBuilder([], SettingsPath);
        builder.Services.AddSingleton<IFileLauncher>(files);
        builder.Services.AddSingleton<ISearchService>(search);
        builder.Services.AddSingleton<IDocumentRepository>(new InMemoryDocumentRepository());
        using var host = builder.Build();
        var status = host.Services.GetRequiredService<CoreStatus>();
        status.SetModelAvailable();
        status.SetReady();
        var viewModel = host.Services.GetRequiredService<SearchViewModel>();
        viewModel.QueryText = "нотатка";

        await viewModel.SearchCommand.ExecuteAsync(null);
        await Assert.Single(viewModel.Results).OpenSourceCommand.ExecuteAsync(null);

        Assert.Equal(result.DocumentPath, Assert.Single(files.Paths));
    }

    [Fact]
    public async Task Start_InvalidSearchOptions_WindowViewModelStillResolves()
    {
        var builder = AppHost.CreateBuilder([], SettingsPath);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{SearchOptions.SectionName}:{nameof(SearchOptions.DefaultTopK)}"] = "0",
        });
        using var host = builder.Build();

        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
        // Program.Main перетворює помилку ValidateOnStart на стан ядра й відкриває вікно.
        host.Services.GetRequiredService<CoreStatus>().SetFailed("Некоректні налаштування");

        var navigation = host.Services.GetRequiredService<MainViewModel>();
        var search = Assert.IsType<SearchViewModel>(navigation.CurrentPage);
        Assert.Equal(SearchQuery.DefaultTopK, search.TopK);
        Assert.Contains("Некоректні налаштування", navigation.NoticeText, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_ValidatesContainer_AndResolvesServicesForViewModels()
    {
        using var host = BuildHost(Path.Combine(_folder, "app.db"));

        Assert.NotNull(host.Services.GetRequiredService<ISearchService>());
        Assert.NotNull(host.Services.GetRequiredService<IIndexingService>());
        Assert.NotNull(host.Services.GetRequiredService<IDocumentRepository>());
        Assert.NotNull(host.Services.GetRequiredService<MainViewModel>());
        Assert.NotSame(host.Services.GetRequiredService<SearchViewModel>(), host.Services.GetRequiredService<SearchViewModel>());
        Assert.NotSame(host.Services.GetRequiredService<LibraryViewModel>(), host.Services.GetRequiredService<LibraryViewModel>());
        Assert.NotSame(host.Services.GetRequiredService<SettingsViewModel>(), host.Services.GetRequiredService<SettingsViewModel>());
    }

    [Fact]
    public async Task Build_UserSettingsFile_IsAppliedOnTopOfDefaults()
    {
        await File.WriteAllTextAsync(SettingsPath, """
            { "LocalMind": { "App": { "Theme": "Dark" }, "Search": { "DefaultTopK": 25, "DefaultMode": "Keyword" } } }
            """);
        using var host = AppHost.CreateBuilder([], SettingsPath).Build();

        Assert.Equal(ThemePreference.Dark, host.Services.GetRequiredService<IOptions<AppOptions>>().Value.Theme);
        var search = host.Services.GetRequiredService<SearchViewModel>();
        Assert.Equal(25m, search.TopK);
        Assert.Equal(SearchMode.Keyword, search.SelectedMode);
    }

    [Fact]
    public void Build_NavigationUsesRegisteredPages_WithExpectedLifetimes()
    {
        var builder = AppHost.CreateBuilder([], SettingsPath);
        // Підміна доводить, що навігація отримує VM з контейнера, а не створює їх через new.
        var search = new FakeSearchService().CreateViewModel();
        var library = new FakeIndexingService().CreateViewModel();
        var account = new FakeAccountClient().CreateViewModel();
        var settings = new FakeUserSettingsService().CreateViewModel();
        builder.Services.AddSingleton(search);
        builder.Services.AddSingleton(library);
        builder.Services.AddSingleton(account);
        builder.Services.AddSingleton(settings);
        using var host = builder.Build();

        var navigation = host.Services.GetRequiredService<MainViewModel>();

        Assert.Same(navigation, host.Services.GetRequiredService<MainViewModel>());
        Assert.Collection(navigation.NavigationItems,
            item => Assert.Same(search, item.Page),
            item => Assert.Same(library, item.Page),
            item => Assert.Same(account, item.Page),
            item => Assert.Same(settings, item.Page));
    }

    [Fact]
    public void Build_WithoutServerUrl_AccountPageReportsNotConfigured()
    {
        using var host = AppHost.CreateBuilder([], SettingsPath).Build();

        var account = host.Services.GetRequiredService<AccountViewModel>();

        Assert.False(account.IsConfigured);
        Assert.False(account.IsSignedIn);
        Assert.Contains("не налаштовано", account.ServerStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Build_ServerUrlFromUserSettings_AccountPageIsConfigured()
    {
        await File.WriteAllTextAsync(SettingsPath, """
            { "LocalMind": { "Backup": { "ServerUrl": "http://localhost:5133" } } }
            """);
        using var host = AppHost.CreateBuilder([], SettingsPath).Build();

        var account = host.Services.GetRequiredService<AccountViewModel>();

        Assert.True(account.IsConfigured);
        Assert.Equal("http://localhost:5133/", account.ServerUrl);
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

    private IHost BuildHost(string databasePath)
    {
        var builder = AppHost.CreateBuilder([], SettingsPath);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{StorageOptions.SectionName}:{nameof(StorageOptions.DatabasePath)}"] = databasePath,
            [$"{EmbeddingModelOptions.SectionName}:{nameof(EmbeddingModelOptions.ModelPath)}"] = "no-such-folder/missing.onnx",
        });

        return builder.Build();
    }
}
