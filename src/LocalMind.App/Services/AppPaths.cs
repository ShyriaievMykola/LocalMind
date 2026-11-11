using LocalMind.Core.Configuration;

namespace LocalMind.App.Services;

/// <summary>Перетворення шляхів з налаштувань на абсолютні: застосунок не залежить від поточної папки.</summary>
internal static class AppPaths
{
    /// <summary>Файл, за яким упізнаємо корінь репозиторію.</summary>
    internal const string RepositoryMarker = "LocalMind.slnx";

    /// <summary>
    /// Чи шукати корінь репозиторію. Лише в Debug: у розробці модель лежить у <c>models/</c> репозиторію,
    /// а встановлена програма не має повода ходити вгору по дереву папок.
    /// </summary>
#if DEBUG
    internal const bool SearchRepositoryByDefault = true;
#else
    internal const bool SearchRepositoryByDefault = false;
#endif

    /// <summary>Тека даних користувача (поруч із базою): <c>%LOCALAPPDATA%\LocalMind</c> тощо.</summary>
    public static string UserDataDirectory() => Path.GetDirectoryName(StorageOptions.DefaultDatabasePath())!;
    
    /// <summary>Файл налаштувань користувача — у теці даних, поруч із базою.</summary>
    public static string UserSettingsPath() => Path.Combine(UserDataDirectory(), "settings.json");

    /// <summary>
    /// Відносний шлях до файла застосунку (модель, токенізатор). Шукає по черзі:
    /// <list type="number">
    /// <item><paramref name="baseDirectory"/> — модель, покладена поруч із програмою;</item>
    /// <item><paramref name="userDataDirectory"/> — модель, завантажена для встановленої програми;</item>
    /// <item>лише в Debug (<paramref name="searchRepository"/>) — корінь репозиторію (папка з <see cref="RepositoryMarker"/>),
    /// якщо програма запущена з нього: туди кладе <c>models/</c> скрипт <c>scripts/download-models</c>.</item>
    /// </list>
    /// Інші батьківські папки не перевіряємо, інакше чужий <c>C:\models\...</c> підмінив би модель.
    /// Якщо файла немає — шлях відносно <paramref name="baseDirectory"/>, щоб у помилці було видно, де його чекали.
    /// </summary>
    public static string ResolveAppFile(
        string path,
        string baseDirectory,
        string? userDataDirectory = null,
        bool searchRepository = SearchRepositoryByDefault)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
        {
            return path;
        }

        var inBase = Path.GetFullPath(Path.Combine(baseDirectory, path));
        string?[] roots = [userDataDirectory, searchRepository ? FindRepositoryRoot(baseDirectory) : null];
        foreach (var root in roots)
        {
            if (File.Exists(inBase) || root is null)
            {
                continue;
            }

            var candidate = Path.GetFullPath(Path.Combine(root, path));
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return inBase;
    }

    private static string? FindRepositoryRoot(string baseDirectory)
    {
        for (var directory = new DirectoryInfo(baseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, RepositoryMarker)))
            {
                return directory.FullName;
            }
        }

        return null;
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
