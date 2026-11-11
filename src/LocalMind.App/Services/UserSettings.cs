using LocalMind.Core.Models;

namespace LocalMind.App.Services;

/// <summary>Налаштування, які користувач змінює на сторінці «Налаштування».</summary>
/// <param name="Theme">Тема інтерфейсу.</param>
/// <param name="DefaultSearchMode">Режим пошуку за замовчуванням.</param>
/// <param name="DefaultTopK">Кількість результатів за замовчуванням.</param>
/// <param name="ModelPath">Шлях до файла ONNX-моделі.</param>
/// <param name="ChunkSizeTokens">Розмір фрагмента в токенах.</param>
/// <param name="OverlapPercent">Перекриття фрагментів, %.</param>
public sealed record UserSettings(
    ThemePreference Theme,
    SearchMode DefaultSearchMode,
    int DefaultTopK,
    string ModelPath,
    int ChunkSizeTokens,
    int OverlapPercent);

/// <summary>Шлях до файла налаштувань користувача.</summary>
public sealed record UserSettingsFile(string Path);