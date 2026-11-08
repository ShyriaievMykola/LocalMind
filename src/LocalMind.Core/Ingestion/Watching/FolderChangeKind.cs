namespace LocalMind.Core.Ingestion.Watching;

/// <summary>Що треба зробити зі шляхом із черги <see cref="FolderWatcher"/>.</summary>
public enum FolderChangeKind
{
    /// <summary>
    /// Файл створено, змінено, перейменовано або видалено. Чи він новий, змінений чи видалений,
    /// визначає споживач черги, порівнявши файл зі сховищем за хешем (ChangeDetector).
    /// </summary>
    FileChanged,

    /// <summary>
    /// Пересканувати всю папку: події могли загубитися (переповнення буфера) або папку перенесли,
    /// перейменували чи видалили цілком — про файли всередині система окремо не повідомляє.
    /// </summary>
    RescanRequired,
}