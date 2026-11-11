using LocalMind.Core.Configuration;

namespace LocalMind.App.Services;

/// <summary>Перетворення шляхів з налаштувань на абсолютні: застосунок не залежить від поточної папки.</summary>
internal static class AppPaths
{
    /// <summary>Файл, за яким упізнаємо корінь репозиторію.</summary>
    internal const string RepositoryMarker = "LocalMind.slnx";

    /// <summary>
    /// Відносний шлях до файла застосунку (модель, токенізатор). Шукає файл у <paramref name="baseDirectory"/>,
    /// а якщо програма запущена з репозиторію — ще й у його корені (папка з <see cref="RepositoryMarker"/>):
    /// так під час розробки знаходиться <c>models/</c>, куди їх кладе <c>scripts/download-models</c>.
    /// Інші батьківські папки не перевіряємо, інакше чужий <c>C:\models\...</c> підмінив би модель.
    /// Якщо файла немає — шлях відносно <paramref name="baseDirectory"/>, щоб у помилці було видно, де його чекали.
    /// </summary>
    public static string ResolveAppFile(string path, string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
        {
            return path;
        }

        var inBase = Path.GetFullPath(Path.Combine(baseDirectory, path));
        if (File.Exists(inBase))
        {
            return inBase;
        }

        var repository = FindRepositoryRoot(baseDirectory);
        if (repository is not null)
        {
            var inRepository = Path.GetFullPath(Path.Combine(repository, path));
            if (File.Exists(inRepository))
            {
                return inRepository;
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
