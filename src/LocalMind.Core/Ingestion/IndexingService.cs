using System.Threading.Channels;
using LocalMind.Core.Abstractions;
using LocalMind.Core.Ingestion.Parsers;
using LocalMind.Core.Ingestion.Watching;
using LocalMind.Core.Models;
using Microsoft.Extensions.Logging;

namespace LocalMind.Core.Ingestion;

/// <summary>
/// Фонова індексація бібліотеки. Файли ставляться в чергу і обробляються по одному:
/// <see cref="ChangeDetector"/> → парсер → фрагменти → embeddings → збереження в одній транзакції →
/// оновлення <see cref="IVectorIndex"/>. Зміни у відстежуваних папках надходять від <see cref="FolderWatcher"/>.
/// </summary>
public sealed partial class IndexingService : IIndexingService, IDisposable
{
    // Windows і macOS за замовчуванням не розрізняють регістр у шляхах, Linux розрізняє.
    private static readonly bool IgnorePathCase = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();
    private static readonly StringComparer PathComparer = IgnorePathCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static readonly StringComparison PathComparison = IgnorePathCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>Усі файли папки й підпапок; недоступні підпапки пропускаються без винятку.</summary>
    private static readonly EnumerationOptions AllFiles = new() { RecurseSubdirectories = true, IgnoreInaccessible = true };

    /// <summary>Скільки чекати завершення фонових циклів під час <see cref="Dispose"/>.</summary>
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);
    
    /// <summary>Пауза між повторними спробами прочитати зайнятий файл за замовчуванням.</summary>
    public static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>Скільки разів повторювати читання файла, зайнятого іншим процесом.</summary>
    private const int MaxRetries = 3;

    /// <summary>
    /// Хеш для документа, файл якого так і не вдалося прочитати: нулі не збігаються з хешем жодного вмісту,
    /// тому наступна перевірка спробує проіндексувати файл знову.
    /// </summary>
    private static readonly string UnknownContentHash = new('0', Document.ContentHashLength);

    private readonly ParserRegistry _parsers;
    private readonly IChunker _chunker;
    private readonly IEmbedder _embedder;
    private readonly IDocumentRepository _repository;
    private readonly IVectorIndex _vectorIndex;
    private readonly ChangeDetector _changeDetector;
    private readonly FolderWatcher _watcher;
    private readonly ILogger<IndexingService> _logger;
    private readonly TimeSpan _retryDelay;

    private readonly Channel<string> _queue = Channel.CreateUnbounded<string>();
    private readonly Lock _lock = new();
    private readonly HashSet<string> _queued = new(PathComparer);
    private readonly HashSet<string> _folders = new(PathComparer);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _processing;
    private readonly Task _watching;

    private CancellationTokenSource? _current;
    private string? _currentFile;
    private int _total;
    private int _processed;
    private int _failed;
    private IndexingProgress _progress = new(0, 0);
    private bool _disposed;

    public IndexingService(
        ParserRegistry parsers,
        IChunker chunker,
        IEmbedder embedder,
        IDocumentRepository repository,
        IVectorIndex vectorIndex,
        ChangeDetector changeDetector,
        FolderWatcher watcher,
        ILogger<IndexingService> logger,
        TimeSpan? retryDelay = null)
    {
        ArgumentNullException.ThrowIfNull(parsers);
        ArgumentNullException.ThrowIfNull(chunker);
        ArgumentNullException.ThrowIfNull(embedder);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(vectorIndex);
        ArgumentNullException.ThrowIfNull(changeDetector);
        ArgumentNullException.ThrowIfNull(watcher);
        ArgumentNullException.ThrowIfNull(logger);

        _parsers = parsers;
        _chunker = chunker;
        _embedder = embedder;
        _repository = repository;
        _vectorIndex = vectorIndex;
        _changeDetector = changeDetector;
        _watcher = watcher;
        _logger = logger;       
        var delay = retryDelay ?? DefaultRetryDelay;
        ArgumentOutOfRangeException.ThrowIfLessThan(delay, TimeSpan.Zero, nameof(retryDelay));
        _retryDelay = delay;

        _processing = Task.Run(() => ProcessQueueAsync(_shutdown.Token));
        _watching = Task.Run(() => ConsumeWatcherAsync(_shutdown.Token));
    }

    /// <inheritdoc />
    public IndexingProgress Progress => Volatile.Read(ref _progress);

    /// <inheritdoc />
    public event EventHandler<IndexingProgress>? ProgressChanged;

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> GetFoldersAsync(CancellationToken cancellationToken = default) =>
        _repository.GetSourceFoldersAsync(cancellationToken);

    /// <inheritdoc />
    public async Task AddFolderAsync(string path, CancellationToken cancellationToken = default)
    {
        var folder = NormalizeFolder(path);
        if (!Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException($"Папку не знайдено: {folder}");
        }

        await _repository.AddSourceFolderAsync(folder, cancellationToken);
        StartWatching(folder);
        await EnqueueFolderAsync(folder, cancellationToken);
    }

    /// <inheritdoc />
    public async Task RemoveFolderAsync(string path, CancellationToken cancellationToken = default)
    {
        var folder = NormalizeFolder(path);

        lock (_lock)
        {
            _folders.Remove(folder);
        }

        _watcher.Unwatch(folder);
        RemoveVectors(await _repository.RemoveSourceFolderAsync(folder, cancellationToken));
    }

    /// <inheritdoc />
    public async Task RescanAsync(CancellationToken cancellationToken = default)
    {
        foreach (var folder in await _repository.GetSourceFoldersAsync(cancellationToken))
        {
            if (!Directory.Exists(folder))
            {
                // Папка може бути тимчасово недоступна (від'єднаний диск), тому документи не видаляємо.
                LogFolderMissing(_logger, folder);
                continue;
            }

            StartWatching(folder);
            await EnqueueFolderAsync(folder, cancellationToken);
        }
    }

    /// <inheritdoc />
    public void CancelPending()
    {
        IndexingProgress progress;

        lock (_lock)
        {
            while (_queue.Reader.TryRead(out _))
            {
                // Вичерпуємо чергу: ці файли вже не оброблятимуться.
            }

            _queued.Clear();
            _current?.Cancel();

            // Інваріант: _total = _processed + (поточний файл ? 1 : 0) + файли в черзі.
            // Черга тепер порожня, тож лишається лише поточний файл. Коли він завершиться, Finish зробить
            // _processed++ і лічильник стане рівно _total: поточний файл уже врахований у «+ 1».
            _total = _processed + (_current is null ? 0 : 1);
            progress = UpdateProgress();
        }

        RaiseProgress(progress);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _current?.Cancel();
        }

        _shutdown.Cancel();
        _queue.Writer.TryComplete();

        // Чекаємо фонові цикли, щоб вони не звертались до звільнених ресурсів.
        _ = Task.WaitAll([_processing, _watching], ShutdownTimeout);

        lock (_lock)
        {
            _current?.Dispose();
            _current = null;
        }

        _shutdown.Dispose();
    }

    private void StartWatching(string folder)
    {
        lock (_lock)
        {
            _folders.Add(folder);
        }

        _watcher.Watch(folder);
    }

    /// <summary>
    /// Ставить у чергу всі підтримувані файли під шляхом і всі документи, що там лежали.
    /// Що з кожним робити (індексувати, пропустити, видалити), вирішить <see cref="ChangeDetector"/>.
    /// Шлях може вже не існувати — тоді в черзі опиняться лише документи, які треба прибрати.
    /// </summary>
    private async Task EnqueueFolderAsync(string path, CancellationToken cancellationToken)
    {
        List<string> paths = Directory.Exists(path)
            ? [.. Directory.EnumerateFiles(path, "*", AllFiles).Where(f => _parsers.TryGetParser(f, out _)).Order(StringComparer.Ordinal)]
            : [];

        var documents = await _repository.GetDocumentsAsync(cancellationToken);
        paths.AddRange(documents.Select(d => d.Path).Where(p => IsInside(p, path)));

        Enqueue(paths);
    }

    private void Enqueue(IReadOnlyCollection<string> paths)
    {
        IndexingProgress progress;

        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            var added = false;
            foreach (var path in paths)
            {
                if (!IsTracked(path) || !_queued.Add(path))
                {
                    continue;
                }

                if (_processed == _total)
                {
                    // Попередня хвиля завершилась — прогрес нової рахуємо з нуля.
                    _processed = 0;
                    _total = 0;
                    _failed = 0;
                }

                _total++;
                _queue.Writer.TryWrite(path);
                added = true;
            }

            if (!added)
            {
                return;
            }

            progress = UpdateProgress();
        }

        RaiseProgress(progress);
    }

    private async Task ProcessQueueAsync(CancellationToken shutdown)
    {
        try
        {
            await foreach (var path in _queue.Reader.ReadAllAsync(shutdown))
            {
                if (TryStart(path) is not { } token)
                {
                    continue;
                }

                var succeeded = await ProcessFileAsync(path, token);
                Finish(succeeded);
            }
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            // Сервіс зупиняється.
        }
    }

    /// <summary>Бере файл у роботу; повертає токен скасування цього файла або <see langword="null"/>, якщо файл пропущено.</summary>
    private CancellationToken? TryStart(string path)
    {
        CancellationToken? token = null;
        IndexingProgress progress;

        lock (_lock)
        {
            // Файл могли прибрати з черги через CancelPending.
            if (!_queued.Remove(path))
            {
                return null;
            }

            if (IsTracked(path))
            {
                _current = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
                _currentFile = path;
                token = _current.Token;
            }
            else
            {
                // Папку прибрали з бібліотеки, поки файл чекав у черзі.
                _processed++;
            }

            progress = UpdateProgress();
        }

        RaiseProgress(progress);
        return token;
    }

    private void Finish(bool succeeded)
    {
        IndexingProgress progress;

        lock (_lock)
        {
            _current?.Dispose();
            _current = null;
            _currentFile = null;
            _processed++;
            if (!succeeded)
            {
                _failed++;
            }

            progress = UpdateProgress();
        }

        RaiseProgress(progress);
    }

    /// <summary>
    /// Обробляє один файл. Файл, зайнятий іншим процесом, читається повторно до <see cref="MaxRetries"/> разів;
    /// будь-яка інша помилка (парсер, нарізка, embeddings, сховище) не зупиняє індексацію решти —
    /// документ отримує статус <see cref="DocumentStatus.Error"/> з причиною.
    /// </summary>
    /// <returns><see langword="false"/>, якщо файл не вдалося проіндексувати.</returns>
    private async Task<bool> ProcessFileAsync(string path, CancellationToken cancellationToken)
    {
        FileChange? change = null;
        try
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    change = await _changeDetector.DetectAsync(path, cancellationToken);
                    await ApplyChangeAsync(change, cancellationToken);
                    return true;
                }
                catch (IOException exception) when (IsFileBusy(exception) && attempt <= MaxRetries)
                {
                    LogFileBusy(_logger, path, attempt, MaxRetries);
                    await Task.Delay(_retryDelay, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Скасовано через CancelPending або зупинку сервісу: уже збережене лишається, це не помилка.
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogFileFailed(_logger, path, exception);
            await SaveErrorAsync(path, change, exception.Message, cancellationToken);
            return false;
        }
    }

    private async Task ApplyChangeAsync(FileChange change, CancellationToken cancellationToken)
    {
        switch (change.Kind)
        {
            case FileChangeKind.New:
            case FileChangeKind.Modified:
                await IndexFileAsync(change, cancellationToken);
                break;

            case FileChangeKind.Deleted when change.StoredDocument is not null:
                RemoveVectors(await _repository.DeleteAsync(change.StoredDocument.Id, cancellationToken));
                break;
        }
    }

    private async Task IndexFileAsync(FileChange change, CancellationToken cancellationToken)
    {
        if (!_parsers.TryGetParser(change.Path, out var parser) || parser is null)
        {
            return;
        }

        // Той самий файл, записаний в іншому регістрі, має замінити стару версію, а не стати другим документом.
        var path = change.StoredDocument?.Path ?? change.Path;
        var hash = change.ContentHash!;
        var id = Guid.NewGuid();

        ParsedDocument parsed;
        long length;
        await using (var stream = new FileStream(
            change.Path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 81_920,
            FileOptions.Asynchronous))
        {
            length = stream.Length;
            parsed = await parser.ParseAsync(stream, cancellationToken);
        }

        var chunks = parsed.HasText ? _chunker.Split(id, parsed.Text) : [];
        if (chunks.Count == 0)
        {
            var status = length == 0 ? DocumentStatus.Empty : DocumentStatus.NoText;
            await SaveAsync(new Document(id, path, hash, status, DateTimeOffset.UtcNow), [], [], cancellationToken);
            return;
        }

        // Батчі всередині embedder-а: він сам ділить набір на частини розміру з налаштувань моделі.
        var vectors = await _embedder.EmbedAsync([.. chunks.Select(c => c.Text)], EmbeddingPurpose.Passage, cancellationToken);
        var document = new Document(id, path, hash, DocumentStatus.Indexed, DateTimeOffset.UtcNow);
        await SaveAsync(document, chunks, vectors, cancellationToken);
    }

    /// <summary>Зберігає документ зі статусом Error і причиною — щоб користувач бачив, що файл не проіндексовано.</summary>
    private async Task SaveErrorAsync(string path, FileChange? change, string reason, CancellationToken cancellationToken)
    {
        // Файл видалено або він не змінився — зберігати помилку нема для чого.
        if (change is { Kind: not (FileChangeKind.New or FileChangeKind.Modified) })
        {
            return;
        }

        var document = new Document(
            Guid.NewGuid(),
            change?.StoredDocument?.Path ?? path,
            change?.ContentHash ?? UnknownContentHash,
            DocumentStatus.Error,
            DateTimeOffset.UtcNow,
            string.IsNullOrWhiteSpace(reason) ? "Невідома помилка." : reason);

        try
        {
            await SaveAsync(document, [], [], cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Скасовано — статус збережемо під час наступної перевірки.
        }
        catch (Exception exception)
        {
            LogErrorNotSaved(_logger, path, exception);
        }
    }

    /// <summary>Файл зайнятий іншим процесом: це <see cref="IOException"/>, але не «файл чи папку не знайдено».</summary>
    private static bool IsFileBusy(IOException exception) =>
        exception is not (FileNotFoundException or DirectoryNotFoundException);

    /// <summary>Зберігає документ в одній транзакції і синхронізує векторний індекс.</summary>
    private async Task SaveAsync(
        Document document,
        IReadOnlyList<Chunk> chunks,
        IReadOnlyList<ReadOnlyMemory<float>> vectors,
        CancellationToken cancellationToken)
    {
        var replaced = await _repository.SaveAsync(document, chunks, vectors, _embedder.ModelName, cancellationToken);

        RemoveVectors(replaced);
        for (var i = 0; i < chunks.Count; i++)
        {
            _vectorIndex.Add(chunks[i].Id, vectors[i]);
        }
    }

    private void RemoveVectors(IEnumerable<Guid> chunkIds)
    {
        foreach (var chunkId in chunkIds)
        {
            _vectorIndex.Remove(chunkId);
        }
    }

    private async Task ConsumeWatcherAsync(CancellationToken shutdown)
    {
        try
        {
            await foreach (var change in _watcher.Changes.ReadAllAsync(shutdown))
            {
                try
                {
                    if (change.Kind == FolderChangeKind.FileChanged)
                    {
                        Enqueue([change.Path]);
                    }
                    else
                    {
                        await EnqueueFolderAsync(change.Path, shutdown);
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    LogRescanFailed(_logger, change.Path, exception.Message);
                }
            }
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            // Сервіс зупиняється.
        }
    }

    /// <summary>Знімок прогресу; викликається під <see cref="_lock"/>.</summary>
    private IndexingProgress UpdateProgress()
    {
        var progress = new IndexingProgress(_processed, _total, _failed, _currentFile);
        Volatile.Write(ref _progress, progress);
        return progress;
    }

    private void RaiseProgress(IndexingProgress progress)
    {
        try
        {
            ProgressChanged?.Invoke(this, progress);
        }
        catch (Exception exception)
        {
            // Помилка в обробнику (наприклад, в UI) не повинна зупиняти індексацію.
            LogProgressHandlerFailed(_logger, exception);
        }
    }

    /// <summary>Чи лежить шлях у відстежуваній папці; викликається під <see cref="_lock"/>.</summary>
    private bool IsTracked(string path) => _folders.Any(folder => IsInside(path, folder));

    private static string NormalizeFolder(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    private static bool IsInside(string path, string folder) =>
        path.Equals(folder, PathComparison)
        || (path.StartsWith(folder, PathComparison)
            && (folder[^1] is '/' or '\\' || path[folder.Length] is '/' or '\\'));

    [LoggerMessage(Level = LogLevel.Warning, Message = "Файл не проіндексовано: {Path}")]
    private static partial void LogFileFailed(ILogger logger, string path, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Файл зайнятий іншим процесом, повтор {Attempt} з {MaxRetries}: {Path}")]
    private static partial void LogFileBusy(ILogger logger, string path, int attempt, int maxRetries);

    [LoggerMessage(Level = LogLevel.Error, Message = "Не вдалося зберегти статус помилки для {Path}")]
    private static partial void LogErrorNotSaved(ILogger logger, string path, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Обробник ProgressChanged завершився з помилкою")]
    private static partial void LogProgressHandlerFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Папку не знайдено, її документи залишено без змін: {Path}")]
    private static partial void LogFolderMissing(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Не вдалося пересканувати {Path}. {Reason}")]
    private static partial void LogRescanFailed(ILogger logger, string path, string reason);
}