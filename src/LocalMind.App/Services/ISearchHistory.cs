namespace LocalMind.App.Services;

/// <summary>Історія пошукових запитів — для підказок під полем пошуку.</summary>
public interface ISearchHistory
{
    /// <summary>Останні запити, найновіший перший.</summary>
    IReadOnlyList<string> Items { get; }

    /// <summary>Додає запит нагору; повтор (без урахування регістру) переміщується, а не дублюється.</summary>
    Task AddAsync(string query, CancellationToken cancellationToken = default);

    /// <summary>Очищує історію, зокрема у файлі.</summary>
    Task ClearAsync(CancellationToken cancellationToken = default);
}

/// <summary>Шлях до файла історії запитів.</summary>
public sealed record SearchHistoryFile(string Path);