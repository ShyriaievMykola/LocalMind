using LocalMind.App.Services;
using LocalMind.Core.Configuration;
using LocalMind.Core.Models;
using LocalMind.Search.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace LocalMind.App.Tests;

public sealed class UserSettingsServiceTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("localmind-settings-").FullName;
    private readonly string _modelPath;

    public UserSettingsServiceTests()
    {
        _modelPath = Path.Combine(_folder, "model.onnx");
        File.WriteAllText(_modelPath, "не справжня модель");
    }

    private string SettingsPath => Path.Combine(_folder, "nested", "settings.json");

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Current_ReflectsActiveOptions()
    {
        var current = CreateService().Current;

        Assert.Equal(new UserSettings(ThemePreference.Dark, SearchMode.Hybrid, 7, _modelPath, 200, 10), current);
    }

    [Fact]
    public void Validate_ValidSettings_HasNoErrors()
    {
        Assert.Empty(CreateService().Validate(Valid()));
    }

    [Fact]
    public void Validate_InvalidValues_ReportsEachProblem()
    {
        var invalid = Valid() with
        {
            DefaultTopK = 0,
            OverlapPercent = 80,
            ModelPath = Path.Combine(_folder, "missing.onnx"),
        };

        var errors = CreateService().Validate(invalid);

        Assert.Contains(errors, e => e.Contains("DefaultTopK", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("OverlapPercent", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("missing.onnx", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_ChunkLargerThanModelLimit_ReportsError()
    {
        var errors = CreateService().Validate(Valid() with { ChunkSizeTokens = 1000 });

        Assert.Contains(errors, e => e.Contains("MaxTokens", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Save_ThenConfigurationFromFile_RestoresSameValues()
    {
        var settings = Valid() with
        {
            Theme = ThemePreference.Light,
            DefaultSearchMode = SearchMode.Keyword,
            DefaultTopK = 25,
            ChunkSizeTokens = 300,
            OverlapPercent = 20,
        };

        await CreateService().SaveAsync(settings);

        // Так файл прочитає AppHost під час наступного запуску.
        var configuration = new ConfigurationBuilder().AddJsonFile(SettingsPath).Build();
        var app = configuration.GetSection(AppOptions.SectionName).Get<AppOptions>()!;
        var search = configuration.GetSection(SearchOptions.SectionName).Get<SearchOptions>()!;
        var chunking = configuration.GetSection(ChunkingOptions.SectionName).Get<ChunkingOptions>()!;
        var model = configuration.GetSection(EmbeddingModelOptions.SectionName).Get<EmbeddingModelOptions>()!;

        Assert.Equal(ThemePreference.Light, app.Theme);
        Assert.Equal(SearchMode.Keyword, search.DefaultMode);
        Assert.Equal(25, search.DefaultTopK);
        Assert.Equal(300, chunking.ChunkSizeTokens);
        Assert.Equal(20, chunking.OverlapPercent);
        Assert.Equal(_modelPath, model.ModelPath);
    }

    [Fact]
    public async Task Save_InvalidSettings_ThrowsAndDoesNotWriteFile()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => CreateService().SaveAsync(Valid() with { DefaultTopK = 0 }));

        Assert.False(File.Exists(SettingsPath));
    }

    private UserSettings Valid() => new(ThemePreference.System, SearchMode.Hybrid, 10, _modelPath, 256, 15);

    private UserSettingsService CreateService()
    {
        var model = Options.Create(new EmbeddingModelOptions { ModelPath = _modelPath, MaxTokens = 512 });
        return new UserSettingsService(
            Options.Create(new AppOptions { Theme = ThemePreference.Dark }),
            Options.Create(new SearchOptions { DefaultTopK = 7 }),
            Options.Create(new ChunkingOptions { ChunkSizeTokens = 200, OverlapPercent = 10 }),
            model,
            [new SearchOptionsValidator()],
            [new ChunkingOptionsValidator(), new ChunkSizeWithinModelLimitValidator(model)],
            new UserSettingsFile(SettingsPath));
    }
}