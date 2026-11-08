using LocalMind.Core.Models;

namespace LocalMind.Core.Ingestion.Watching;

/// <summary>Результат перевірки файла в <see cref="ChangeDetector"/>.</summary>
/// <param name="Path">Повний шлях до файла.</param>
/// <param name="Kind">Що змінилося.</param>
/// <param name="ContentHash">
/// SHA-256 поточного вмісту; <see langword="null"/> для <see cref="FileChangeKind.Deleted"/>.
/// </param>
/// <param name="StoredDocument">
/// Документ зі сховища; <see langword="null"/> для <see cref="FileChangeKind.New"/>, а також для
/// <see cref="FileChangeKind.Deleted"/>, якщо файла не було й у сховищі.
/// </param>
public sealed record FileChange(string Path, FileChangeKind Kind, string? ContentHash, Document? StoredDocument)
{
    /// <summary>Чи треба (пере)індексувати файл: він новий або змінений.</summary>
    public bool NeedsIndexing => Kind is FileChangeKind.New or FileChangeKind.Modified;
}