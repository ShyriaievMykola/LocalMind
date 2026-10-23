using LocalMind.Core.Models;

namespace LocalMind.Core.Abstractions;

/// <summary>
/// Фонова індексація: стежить за папками бібліотеки і підтримує індекс в актуальному стані.
/// Помилка в окремому файлі не зупиняє індексацію — документ отримує статус <see cref="DocumentStatus.Error"/>.
/// </summary>
public interface IIndexingService
{
    /// <summary>Поточний стан індексації.</summary>
    IndexingProgress Progress { get; }

    /// <summary>
    /// Стан індексації змінився. Викликається з фонового потоку — UI має сам перейти у свій потік.
    /// </summary>
    event EventHandler<IndexingProgress>? ProgressChanged;

    /// <summary>Відстежувані папки.</summary>
    Task<IReadOnlyList<string>> GetFoldersAsync(CancellationToken cancellationToken = default);

    /// <summary>Додає папку, починає стежити за нею і ставить її файли в чергу індексації.</summary>
    /// <exception cref="DirectoryNotFoundException">Папки не існує.</exception>
    Task AddFolderAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Припиняє стежити за папкою і видаляє її документи з бібліотеки та індексів.</summary>
    Task RemoveFolderAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Перевіряє всі відстежувані папки і ставить у чергу лише нові та змінені файли;
    /// видалені файли прибирає з бібліотеки. Викликається під час запуску застосунку.
    /// </summary>
    Task RescanAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Скасовує індексацію: очищує чергу і зупиняє обробку поточного файла.
    /// Уже проіндексовані документи залишаються доступними.
    /// </summary>
    void CancelPending();
}
