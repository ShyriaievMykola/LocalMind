using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalMind.App.Services;
using LocalMind.Core.Models;
using Microsoft.Extensions.Logging;

namespace LocalMind.App.ViewModels;

/// <summary>
/// Налаштування користувача. Зберігаються в JSON у теці даних (<see cref="IUserSettingsService"/>)
/// і підхоплюються під час наступного запуску; тема застосовується одразу.
/// </summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    private const string RestartMessage = "Збережено. Нові значення почнуть діяти після перезапуску застосунку.";

    private const string ReindexMessage =
        RestartMessage + " Розмір фрагмента, перекриття і модель змінюють індекс: щоб застосувати їх до вже " +
        "проіндексованих файлів, приберіть папку в «Бібліотеці» і додайте її знову.";

    private readonly IUserSettingsService _settings;
    private readonly IThemeService _theme;
    private readonly ILogger<SettingsViewModel> _logger;
    private UserSettings _saved;

    public SettingsViewModel(IUserSettingsService settings, IThemeService theme, ILogger<SettingsViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(logger);

        _settings = settings;
        _theme = theme;
        _logger = logger;
        _saved = settings.Current;
        Show(_saved);
    }

    public IReadOnlyList<ThemePreference> Themes { get; } = Array.AsReadOnly<ThemePreference>([
        ThemePreference.System, ThemePreference.Light, ThemePreference.Dark,
    ]);

    public IReadOnlyList<SearchMode> SearchModes { get; } = Array.AsReadOnly<SearchMode>([
        SearchMode.Semantic, SearchMode.Keyword, SearchMode.Hybrid,
    ]);

    [ObservableProperty]
    public partial ThemePreference Theme { get; set; }

    [ObservableProperty]
    public partial SearchMode DefaultSearchMode { get; set; }

    // NumericUpDown.Value — decimal?; порожнє чи дробове значення відсіюється у VM.
    [ObservableProperty]
    public partial decimal? DefaultTopK { get; set; }

    [ObservableProperty]
    public partial string ModelPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial decimal? ChunkSizeTokens { get; set; }

    [ObservableProperty]
    public partial decimal? OverlapPercent { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrors))]
    public partial IReadOnlyList<string> Errors { get; private set; } = [];

    public bool HasErrors => Errors.Count > 0;

    [ObservableProperty]
    public partial string? SavedMessage { get; private set; }

    [RelayCommand]
    private async Task SaveAsync()
    {
        SavedMessage = null;

        var topK = ToInt(DefaultTopK);
        var chunkSize = ToInt(ChunkSizeTokens);
        var overlap = ToInt(OverlapPercent);
        if (topK is not { } k || chunkSize is not { } size || overlap is not { } percent)
        {
            Errors = [.. NumberErrors(topK, chunkSize, overlap)];
            return;
        }

        var candidate = new UserSettings(Theme, DefaultSearchMode, k, ModelPath.Trim(), size, percent);
        var errors = _settings.Validate(candidate);
        if (errors.Count > 0)
        {
            Errors = errors;
            return;
        }

        try
        {
            await _settings.SaveAsync(candidate);
        }
        catch (Exception exception)
        {
            LogSaveFailed(_logger, exception);
            Errors = [$"Не вдалося зберегти налаштування: {exception.Message}"];
            return;
        }

        Errors = [];
        _theme.Apply(candidate.Theme);
        _saved = candidate;
        Show(candidate);
        SavedMessage = DescribeSaved(_settings.Current, candidate);
    }

    /// <summary>Повертає значення до останніх збережених.</summary>
    [RelayCommand]
    private void Reset()
    {
        Show(_saved);
        Errors = [];
        SavedMessage = null;
    }

    private void Show(UserSettings settings)
    {
        Theme = settings.Theme;
        DefaultSearchMode = settings.DefaultSearchMode;
        DefaultTopK = settings.DefaultTopK;
        ModelPath = settings.ModelPath;
        ChunkSizeTokens = settings.ChunkSizeTokens;
        OverlapPercent = settings.OverlapPercent;
    }

    /// <summary>Що зміниться і коли: порівнюємо з налаштуваннями, з якими застосунок працює зараз.</summary>
    private static string DescribeSaved(UserSettings active, UserSettings saved)
    {
        if (active with { Theme = saved.Theme } == saved)
        {
            return "Збережено. Тему застосовано.";
        }

        var changesIndex = active.ChunkSizeTokens != saved.ChunkSizeTokens
            || active.OverlapPercent != saved.OverlapPercent
            || !string.Equals(active.ModelPath, saved.ModelPath, StringComparison.Ordinal);
        return changesIndex ? ReindexMessage : RestartMessage;
    }

    private static int? ToInt(decimal? value) =>
        value is { } number && decimal.Truncate(number) == number && number is >= int.MinValue and <= int.MaxValue
            ? (int)number
            : null;

    private static IEnumerable<string> NumberErrors(int? topK, int? chunkSize, int? overlap)
    {
        if (topK is null)
        {
            yield return "Кількість результатів має бути цілим числом.";
        }

        if (chunkSize is null)
        {
            yield return "Розмір фрагмента має бути цілим числом.";
        }

        if (overlap is null)
        {
            yield return "Перекриття має бути цілим числом.";
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Не вдалося зберегти налаштування")]
    private static partial void LogSaveFailed(ILogger logger, Exception exception);
}