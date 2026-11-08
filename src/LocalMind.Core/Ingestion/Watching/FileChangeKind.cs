namespace LocalMind.Core.Ingestion.Watching;

/// <summary>Стан файла порівняно з версією у сховищі.</summary>
public enum FileChangeKind
{
    /// <summary>Файла ще немає у сховищі — його треба проіндексувати.</summary>
    New,

    /// <summary>Вміст змінився — файл треба переіндексувати.</summary>
    Modified,

    /// <summary>Вміст той самий — повторна індексація не потрібна.</summary>
    Unchanged,

    /// <summary>Файла немає на диску — документ треба прибрати зі сховища та індексів.</summary>
    Deleted,
}