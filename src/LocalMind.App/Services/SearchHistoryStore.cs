using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace LocalMind.App.Services;

/// <summary>Історія запитів у JSON-файлі в теці даних користувача; переживає перезапуск.</summary>
internal sealed partial class SearchHistoryStore : ISearchHistory, IDisposable
{
    /// <summary>Скільки останніх запитів зберігаємо.</summary>
    public const int MaxItems = 20;

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly SearchHistoryFile _file;
    private readonly ILogger<SearchHistoryStore> _logger;
    private readonly Lock _lock = new();
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private readonly List<string> _items;

    public SearchHistoryStore(SearchHistoryFile file, ILogger<SearchHistoryStore> logger)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(logger);

        _file = file;
        _logger = logger;
        _items = Load();
    }

    public IReadOnlyList<string> Items
    {
        get
        {
            lock (_lock)
            {
                return [.. _items];
            }
        }
    }

    public Task AddAsync(string query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var text = query.Trim();
        if (text.Length == 0)
        {
            return Task.CompletedTask;
        }

        string[] snapshot;
        lock (_lock)
        {
            _items.RemoveAll(item => item.Equals(text, StringComparison.OrdinalIgnoreCase));
            _items.Insert(0, text);
            if (_items.Count > MaxItems)
            {
                _items.RemoveRange(MaxItems, _items.Count - MaxItems);
            }

            snapshot = [.. _items];
        }

        return SaveAsync(snapshot, cancellationToken);
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _items.Clear();
        }

        return SaveAsync([], cancellationToken);
    }

    public void Dispose() => _saveLock.Dispose();

    private List<string> Load()
    {
        try
        {
            if (!File.Exists(_file.Path))
            {
                return [];
            }

            var items = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_file.Path)) ?? [];
            return [.. items
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxItems)];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // Пошкоджений чи недоступний файл історії не повинен заважати пошуку — починаємо з порожньої.
            LogLoadFailed(_logger, _file.Path, exception);
            return [];
        }
    }

    /// <summary>Записи по черзі: два пошуки поспіль не пишуть файл одночасно.</summary>
    private async Task SaveAsync(string[] items, CancellationToken cancellationToken)
    {
        await _saveLock.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file.Path)!);
            var temporary = _file.Path + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(items, WriteOptions), cancellationToken);
            File.Move(temporary, _file.Path, overwrite: true);
        }
        finally
        {
            _saveLock.Release();
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Не вдалося прочитати історію пошуку з {Path}")]
    private static partial void LogLoadFailed(ILogger logger, string path, Exception exception);
}