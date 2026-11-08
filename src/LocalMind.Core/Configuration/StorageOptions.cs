namespace LocalMind.Core.Configuration;

/// <summary>Налаштування локального сховища. Секція конфігурації — <see cref="SectionName"/>.</summary>
public sealed class StorageOptions
{
    /// <summary>Секція конфігурації.</summary>
    public const string SectionName = "LocalMind:Storage";

    /// <summary>Шлях до файла бази; якщо порожній — стандартна тека даних застосунку (<see cref="DefaultDatabasePath"/>).</summary>
    public string DatabasePath { get; set; } = string.Empty;

    /// <summary>
    /// Стандартний шлях до бази: <c>%LOCALAPPDATA%</c> на Windows, <c>~/.local/share</c> на Linux,
    /// <c>~/Library/Application Support</c> на macOS.
    /// </summary>
    public static string DefaultDatabasePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify),
            "LocalMind",
            "localmind.db");
}
