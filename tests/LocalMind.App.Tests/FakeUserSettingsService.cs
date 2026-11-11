using LocalMind.App.Services;
using LocalMind.App.ViewModels;
using LocalMind.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace LocalMind.App.Tests;

/// <summary>Налаштування без файла: запам'ятовує збережене, перевірку задає тест.</summary>
internal sealed class FakeUserSettingsService : IUserSettingsService
{
    public UserSettings Current { get; set; } =
        new(ThemePreference.System, SearchMode.Hybrid, 10, "/models/model.onnx", 256, 15);

    public List<string> ValidationErrors { get; } = [];

    public List<UserSettings> Saved { get; } = [];

    public Exception? SaveError { get; set; }

    public FakeThemeService Theme { get; } = new();

    public IReadOnlyList<string> Validate(UserSettings settings) => [.. ValidationErrors];

    public Task SaveAsync(UserSettings settings, CancellationToken cancellationToken = default)
    {
        if (SaveError is not null)
        {
            return Task.FromException(SaveError);
        }

        Saved.Add(settings);
        return Task.CompletedTask;
    }

    public SettingsViewModel CreateViewModel() => new(this, Theme, NullLogger<SettingsViewModel>.Instance);
}

/// <summary>Тема без Avalonia: запам'ятовує, що застосовано.</summary>
internal sealed class FakeThemeService : IThemeService
{
    public List<ThemePreference> Applied { get; } = [];

    public ThemePreference Preference { get; private set; }

    public void Apply(ThemePreference preference)
    {
        Preference = preference;
        Applied.Add(preference);
    }
}