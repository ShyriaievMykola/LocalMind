using LocalMind.App.Services;
using LocalMind.Core.Models;

namespace LocalMind.App.Tests;

public sealed class SettingsViewModelTests
{
    private readonly FakeUserSettingsService _settings = new();

    [Fact]
    public void Constructor_ShowsActiveSettings()
    {
        var viewModel = _settings.CreateViewModel();

        Assert.Equal(ThemePreference.System, viewModel.Theme);
        Assert.Equal(SearchMode.Hybrid, viewModel.DefaultSearchMode);
        Assert.Equal(10m, viewModel.DefaultTopK);
        Assert.Equal("/models/model.onnx", viewModel.ModelPath);
        Assert.Equal(256m, viewModel.ChunkSizeTokens);
        Assert.Equal(15m, viewModel.OverlapPercent);
        Assert.False(viewModel.HasErrors);
    }

    [Fact]
    public async Task Save_ChangedSearch_SavesAppliesThemeAndAsksForRestart()
    {
        var viewModel = _settings.CreateViewModel();
        viewModel.DefaultTopK = 25;
        viewModel.DefaultSearchMode = SearchMode.Keyword;
        viewModel.Theme = ThemePreference.Dark;

        await viewModel.SaveCommand.ExecuteAsync(null);

        var saved = Assert.Single(_settings.Saved);
        Assert.Equal(
            _settings.Current with { DefaultTopK = 25, DefaultSearchMode = SearchMode.Keyword, Theme = ThemePreference.Dark },
            saved);
        Assert.Equal([ThemePreference.Dark], _settings.Theme.Applied);
        Assert.Contains("перезапуску", viewModel.SavedMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("приберіть папку", viewModel.SavedMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Save_ChunkSizeChanged_ExplainsReindexing()
    {
        var viewModel = _settings.CreateViewModel();
        viewModel.ChunkSizeTokens = 300;

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Contains("приберіть папку", viewModel.SavedMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Save_OnlyTheme_IsAppliedWithoutRestart()
    {
        var viewModel = _settings.CreateViewModel();
        viewModel.Theme = ThemePreference.Light;

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Збережено. Тему застосовано.", viewModel.SavedMessage);
        Assert.Equal([ThemePreference.Light], _settings.Theme.Applied);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1.5)]
    public async Task Save_TopKNotInteger_ShowsErrorWithoutSaving(double? topK)
    {
        var viewModel = _settings.CreateViewModel();
        viewModel.DefaultTopK = topK is null ? null : (decimal)topK.Value;

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Empty(_settings.Saved);
        Assert.True(viewModel.HasErrors);
        Assert.Empty(_settings.Theme.Applied);
        Assert.Null(viewModel.SavedMessage);
    }

    [Fact]
    public async Task Save_ValidationErrors_AreShownAndNothingSaved()
    {
        _settings.ValidationErrors.Add("Файл моделі не знайдено: x.onnx");
        var viewModel = _settings.CreateViewModel();

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(["Файл моделі не знайдено: x.onnx"], viewModel.Errors);
        Assert.Empty(_settings.Saved);
        Assert.Empty(_settings.Theme.Applied);
        Assert.Null(viewModel.SavedMessage);
    }

    [Fact]
    public async Task Save_WriteFails_ShowsErrorAndKeepsTheme()
    {
        _settings.SaveError = new IOException("Диск заповнено");
        var viewModel = _settings.CreateViewModel();
        viewModel.Theme = ThemePreference.Dark;

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Contains(viewModel.Errors, e => e.Contains("Диск заповнено", StringComparison.Ordinal));
        Assert.Empty(_settings.Theme.Applied);
    }

    [Fact]
    public async Task Reset_RestoresLastSavedValues()
    {
        var viewModel = _settings.CreateViewModel();
        viewModel.DefaultTopK = 30;
        await viewModel.SaveCommand.ExecuteAsync(null);

        viewModel.DefaultTopK = 99;
        viewModel.ModelPath = "інший.onnx";
        viewModel.ResetCommand.Execute(null);

        Assert.Equal(30m, viewModel.DefaultTopK);
        Assert.Equal("/models/model.onnx", viewModel.ModelPath);
    }
}