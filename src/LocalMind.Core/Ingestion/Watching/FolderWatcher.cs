using System.Threading.Channels;
using LocalMind.Core.Ingestion.Parsers;

namespace LocalMind.Core.Ingestion.Watching;

/// <summary>
/// Стежить за папками бібліотеки (рекурсивно) і ставить зміни в чергу <see cref="Changes"/>.
/// Кілька подій одного файла протягом інтервалу debounce зливаються в одну. Непідтримувані
/// і тимчасові файли редакторів (<c>~$*</c>, <c>*.tmp</c>, <c>*.swp</c>) ігноруються.
/// </summary>
public sealed class FolderWatcher : IDisposable
{
    /// <summary>Інтервал debounce за замовчуванням.</summary>
    public static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(500);

    /// <summary>Префікс файлів-блокувань Microsoft Office (<c>~$report.docx</c>).</summary>
    private const string OfficeLockFilePrefix = "~$";

    /// <summary>Тимчасові файли редакторів, які ніколи не індексуються.</summary>
    private static readonly string[] _ignoredExtensions = [".tmp", ".swp"];

    private readonly ParserRegistry _parsers;
    private readonly TimeSpan _debounce;
    private readonly Channel<FolderChange> _channel = Channel.CreateUnbounded<FolderChange>();
    private readonly Lock _lock = new();
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.Ordinal);
    private readonly Dictionary<FolderChange, Timer> _pending = [];
    private bool _disposed;

    /// <param name="parsers">Визначає, які файли підтримуються.</param>
    /// <param name="debounce">
    /// Скільки чекати тиші після останньої події, перш ніж поставити зміну в чергу;
    /// за замовчуванням <see cref="DefaultDebounce"/>.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="debounce"/> від'ємний.</exception>
    public FolderWatcher(ParserRegistry parsers, TimeSpan? debounce = null)
    {
        ArgumentNullException.ThrowIfNull(parsers);
        var interval = debounce ?? DefaultDebounce;
        ArgumentOutOfRangeException.ThrowIfLessThan(interval, TimeSpan.Zero, nameof(debounce));

        _parsers = parsers;
        _debounce = interval;
    }

    /// <summary>Черга змін для служби індексації. Після <see cref="Dispose"/> черга завершується.</summary>
    public ChannelReader<FolderChange> Changes => _channel.Reader;
  
    /// <summary>Скільки змін ще чекають завершення debounce (для тестів).</summary>
    internal int PendingCount
    {
        get
        {
            lock (_lock)
            {
                return _pending.Count;
            }
        }
    }

    /// <summary>Починає стежити за папкою і всіма її підпапками; повторний виклик нічого не змінює.</summary>
    /// <exception cref="DirectoryNotFoundException">Папки не існує.</exception>
    /// <exception cref="ObjectDisposedException">Спостерігача вже звільнено.</exception>
    public void Watch(string folder)
    {
        var path = NormalizeFolder(folder);
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"Папку «{path}» не знайдено.");
        }

        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_watchers.ContainsKey(path))
            {
                return;
            }

            var watcher = new FileSystemWatcher(path);
            _watchers.Add(path, watcher);

            watcher.IncludeSubdirectories = true;
            watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                | NotifyFilters.LastWrite | NotifyFilters.Size;
            watcher.InternalBufferSize = 64 * 1024;
            watcher.Created += OnChanged;
            watcher.Changed += OnChanged;
            watcher.Deleted += OnChanged;
            watcher.Renamed += OnRenamed;
            watcher.Error += (_, _) => OnWatcherError(path);
            watcher.EnableRaisingEvents = true;
        }
    }

    /// <summary>Припиняє стежити за папкою; її зміни, що ще чекали debounce, відкидаються.</summary>
    public void Unwatch(string folder)
    {
        var path = NormalizeFolder(folder);
        FileSystemWatcher? watcher;

        lock (_lock)
        {
            _watchers.Remove(path, out watcher);

            foreach (var change in _pending.Keys.Where(c => IsInside(c.Path, path)).ToList())
            {
                if (_pending.Remove(change, out var timer))
                {
                    timer.Dispose();
                }
            }
        }

        watcher?.Dispose();
    }

    public void Dispose()
    {
        List<FileSystemWatcher> watchers;

        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            watchers = [.. _watchers.Values];
            _watchers.Clear();

            foreach (var timer in _pending.Values)
            {
                timer.Dispose();
            }

            _pending.Clear();
            _channel.Writer.TryComplete();
        }

        foreach (var watcher in watchers)
        {
            watcher.Dispose();
        }
    }

    /// <summary>
    /// Помилка спостерігача (найчастіше — переповнення буфера подій): частина подій могла
    /// загубитися, тому вся папка пересканується.
    /// </summary>
    internal void OnWatcherError(string folder) =>
        Schedule(new FolderChange(folder, FolderChangeKind.RescanRequired));

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        switch (e.ChangeType)
        {
            case WatcherChangeTypes.Created:
                OnAppeared(e.FullPath);
                break;
            case WatcherChangeTypes.Deleted:
                OnDisappeared(e.FullPath);
                break;
            default:
                OnModified(e.FullPath);
                break;
        }
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        OnDisappeared(e.OldFullPath);
        OnAppeared(e.FullPath);
    }

    /// <summary>З'явився файл або папка (зокрема перенесені ззовні).</summary>
    private void OnAppeared(string path)
    {
        if (Directory.Exists(path))
        {
            // Для перенесеної папки система повідомляє лише про саму папку, а не про файли в ній.
            Schedule(new FolderChange(path, FolderChangeKind.RescanRequired));
        }
        else
        {
            OnModified(path);
        }
    }

    private void OnModified(string path)
    {
        if (IsSupportedFile(path) && !Directory.Exists(path))
        {
            Schedule(new FolderChange(path, FolderChangeKind.FileChanged));
        }
    }

    /// <summary>Зник файл або папка.</summary>
    private void OnDisappeared(string path)
    {
        if (IsSupportedFile(path))
        {
            Schedule(new FolderChange(path, FolderChangeKind.FileChanged));
        }
        else if (!Path.HasExtension(path))
        {
            // Можливо, видалили папку: про файли всередині система окремо не повідомляє.
            Schedule(new FolderChange(path, FolderChangeKind.RescanRequired));
        }
    }

    private bool IsSupportedFile(string path)
    {
        var name = Path.GetFileName(path);
        if (name.StartsWith(OfficeLockFilePrefix, StringComparison.Ordinal)
            || _ignoredExtensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        return _parsers.TryGetParser(path, out _);
    }

    /// <summary>
    /// Debounce: кожна нова подія того самого шляху відкладає таймер ще на <see cref="_debounce"/>.
    /// Зміна потрапляє в чергу, лише коли події припинилися.
    /// </summary>
    private void Schedule(FolderChange change)
    {
        lock (_lock)
        {
            // Подія могла прийти від спостерігача, якого щойно прибрали через Unwatch.
            if (_disposed || !_watchers.Keys.Any(folder => IsInside(change.Path, folder)))
            {
                return;
            }

            if (_pending.TryGetValue(change, out var timer))
            {
                timer.Change(_debounce, Timeout.InfiniteTimeSpan);
                return;
            }

            _pending.Add(change, new Timer(Flush, change, _debounce, Timeout.InfiniteTimeSpan));
        }
    }

    private void Flush(object? state)
    {
        var change = (FolderChange)state!;

        lock (_lock)
        {
            if (_disposed || !_pending.Remove(change, out var timer))
            {
                return;
            }

            timer.Dispose();
            _channel.Writer.TryWrite(change);
        }
    }

    private static string NormalizeFolder(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
    }

    /// <summary>Чи збігається шлях з папкою або лежить усередині неї.</summary>
    private static bool IsInside(string path, string folder) =>
        path.Equals(folder, StringComparison.Ordinal)
        || (path.StartsWith(folder, StringComparison.Ordinal)
            && (folder[^1] is '/' or '\\' || path[folder.Length] is '/' or '\\'));
}