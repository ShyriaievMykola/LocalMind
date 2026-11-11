using LocalMind.Core.Configuration;

namespace LocalMind.App.Services;

/// <summary>Перетворення шляхів з налаштувань на абсолютні: застосунок не залежить від поточної папки.</summary>
internal static class AppPaths
{
    /// <summary>
    /// Відносний шлях до файла застосунку (модель, токенізатор). Шукає файл у <paramref name="baseDirectory"/>,
    /// потім у батьківських папках — так під час розробки знаходиться <c>models/</c> у корені репозиторію,
    /// куди їх кладе <c>scripts/download-models</c>. Якщо файла ніде немає — шлях відносно <paramref name="baseDirectory"/>,
    /// щоб у повідомленні про помилку було видно, де його чекали.
    /// </summary>
    public static string ResolveAppFile(string path, string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
        {
            return path;
        }

        for (var directory = new DirectoryInfo(baseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.GetFullPath(Path.Combine(directory.FullName, path));
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.GetFullPath(Path.Combine(baseDirectory, path));
    }

    /// <summary>
    /// Шлях до бази: порожній — стандартний (<see cref="StorageOptions.DefaultDatabasePath"/>),
    /// відносний — у теці даних користувача.
    /// </summary>
    public static string ResolveDatabase(string path)
    {
        var defaultPath = StorageOptions.DefaultDatabasePath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return defaultPath;
        }

        return Path.IsPathRooted(path) ? path : Path.Combine(Path.GetDirectoryName(defaultPath)!, path);
    }
}
