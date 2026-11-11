using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;

namespace LocalMind.App.Services;

/// <summary>Вибір теми користувачем. «Система» слідує за темою ОС.</summary>
public enum ThemePreference
{
    System,
    Light,
    Dark,
}

/// <summary>Перемикає тему застосунку без перезапуску. Перемикач у налаштуваннях (#93) викликає <see cref="Apply"/>.</summary>
public interface IThemeService
{
    ThemePreference Preference { get; }

    void Apply(ThemePreference preference);
}

public sealed class ThemeService : IThemeService
{
    public ThemePreference Preference { get; private set; } = ThemePreference.System;

    public void Apply(ThemePreference preference)
    {
        Preference = preference;

        // Application.Current є лише після старту Avalonia; до того тему задає RequestedThemeVariant з App.axaml.
        if (Application.Current is not { } app)
        {
            return;
        }

        var variant = ToVariant(preference);
        if (Dispatcher.UIThread.CheckAccess())
        {
            app.RequestedThemeVariant = variant;
        }
        else
        {
            Dispatcher.UIThread.Post(() => app.RequestedThemeVariant = variant);
        }
    }

    /// <summary>«Система» → <see cref="ThemeVariant.Default"/>: Avalonia сама стежить за зміною теми ОС.</summary>
    internal static ThemeVariant ToVariant(ThemePreference preference) =>
        preference switch
        {
            ThemePreference.Light => ThemeVariant.Light,
            ThemePreference.Dark => ThemeVariant.Dark,
            ThemePreference.System => ThemeVariant.Default,
            _ => throw new ArgumentOutOfRangeException(nameof(preference), preference, null),
        };
}
