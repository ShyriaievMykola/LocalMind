using Avalonia.Data.Converters;
using LocalMind.App.Services;

namespace LocalMind.App.Converters;

/// <summary>Назви тем українською для списку на сторінці налаштувань.</summary>
public static class ThemeConverters
{
    public static FuncValueConverter<ThemePreference, string> Title { get; } = new(theme => theme switch
    {
        ThemePreference.Light => "Світла",
        ThemePreference.Dark => "Темна",
        _ => "Як у системі",
    });
}