using System.Collections.Concurrent;
using System.Diagnostics;
using LocalMind.Core.Abstractions;
using LocalMind.Core.Configuration;
using LocalMind.Core.Ingestion;
using LocalMind.Core.Ingestion.Chunking;
using LocalMind.Core.Ingestion.Parsers;
using LocalMind.Core.Ingestion.Watching;
using LocalMind.Core.Models;
using LocalMind.TestUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LocalMind.Core.Tests.Ingestion;

public sealed class IndexingServiceTests : IDisposable
{
    // Запас на повільні раннери CI; на швидкій машині умови виконуються за долі секунди.
    private static readonly TimeSpan _waitTimeout = TimeSpan.FromSeconds(30);

    private readonly string _folder = Directory.CreateTempSubdirectory("localmind-indexing-").FullName;
    private readonly InMemoryDocumentRepository _repository = new();
    private readonly FakeEmbedder _fakeEmbedder = new();
    private readonly ParserRegistry _parsers = new([new PlainTextParser(), new MarkdownParser(), new DocxParser()]);
    private readonly FakeVectorIndex _vectorIndex;
    private readonly FolderWatcher _watcher;
    private FolderWatcher? _liveWatcher;
    private IndexingService? _service;

    public IndexingServiceTests()
    {
        _vectorIndex = new FakeVectorIndex(_fakeEmbedder.Dimensions);
        // Watcher, що не встигає спрацювати: на macOS FSEvents іноді повідомляє про файли, створені щойно
        // перед Watch, і файл стає в чергу вдруге — тоді Progress.Total не збігається з очікуваним.
        // Тести реакції на події файлової системи створюють власний watcher (LiveWatcher).
        _watcher = new FolderWatcher(_parsers, TimeSpan.FromHours(1));
    }

    public void Dispose()
    {
        _service?.Dispose();
        _watcher.Dispose();
        _liveWatcher?.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public async Task AddFolder_IndexesAllSupportedFiles()
    {
        Write("a.txt", "кава зранку");
        Write(Path.Combine("sub", "b.md"), "# Чай\n\nчай увечері");
        Write("image.png", "не текст");
        var service = CreateService();

        await service.AddFolderAsync(_folder);
        await WaitUntilAsync(() => service.Progress is { IsCompleted: true, Total: 2 });

        var documents = await _repository.GetDocumentsAsync();
        Assert.Equal(2, documents.Count);
        Assert.All(documents, d => Assert.Equal(DocumentStatus.Indexed, d.Status));
        Assert.Equal(_repository.AllChunks.Count, _vectorIndex.Count);
        Assert.Contains(Path.GetFullPath(_folder), await service.GetFoldersAsync());
    }

    [Fact]
    public async Task Progress_IsReportedAndReachesHundredPercent()
    {
        Write("one.txt", "перша нотатка");
        Write("two.txt", "друга нотатка");
        Write("three.txt", "третя нотатка");
        var service = CreateService();
        var reports = new ConcurrentQueue<IndexingProgress>();
        service.ProgressChanged += (_, progress) => reports.Enqueue(progress);

        await service.AddFolderAsync(_folder);
        await WaitUntilAsync(() => reports.Any(p => p is { IsCompleted: true, Total: 3 }));

        Assert.Contains(reports, p => p.CurrentFile is not null);
        Assert.Contains(reports, p => p is { Processed: 3, Total: 3, Failed: 0 });
        Assert.Equal(1.0, service.Progress.Fraction);
    }

    [Fact]
    public async Task CancelPending_StopsProcessingAndServiceKeepsWorking()
    {
        Write("one.txt", "перша");
        Write("two.txt", "друга");
        Write("three.txt", "третя");
        var embedder = new GatedEmbedder(_fakeEmbedder);
        var service = CreateService(embedder);
        await service.AddFolderAsync(_folder);
        await embedder.Started.Task.WaitAsync(_waitTimeout);

        var stopwatch = Stopwatch.StartNew();
        service.CancelPending();
        service.CancelPending();
        await WaitUntilAsync(() => service.Progress is { IsCompleted: true, CurrentFile: null });

        // Скасований файл врахований рівно один раз: оброблено = всього, без переповнення,
        // навіть після повторного CancelPending.
        Assert.Equal(new IndexingProgress(1, 1), service.Progress);

        // Embedder не відпускаємо, тож завершитися вчасно можна лише через скасування; запас — для повільних раннерів.
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10));
        Assert.Empty(await _repository.GetDocumentsAsync());
        Assert.Equal(0, _vectorIndex.Count);

        // Після скасування нова робота обробляється як звичайно.
        embedder.Open();
        await service.RescanAsync();
        await WaitUntilAsync(() => _repository.AllChunks.Count == 3 && _vectorIndex.Count == 3);
    }

    [Fact]
    public async Task Rescan_SkipsUnchangedFiles()
    {
        Write("a.txt", "кава");
        var service = CreateService();
        await service.AddFolderAsync(_folder);
        await WaitUntilAsync(() => service.Progress is { IsCompleted: true, Total: 1 });
        var before = Assert.Single(await _repository.GetDocumentsAsync());
        var embedCalls = _fakeEmbedder.Calls.Count;

        await service.RescanAsync();
        await WaitUntilAsync(() => service.Progress is { IsCompleted: true, Total: 1 });

        var after = Assert.Single(await _repository.GetDocumentsAsync());
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(embedCalls, _fakeEmbedder.Calls.Count);
    }

    [Fact]
    public async Task ModifiedFile_IsReindexedAfterWatcherEvent()
    {
        var path = Write("note.txt", "стара версія");
        var service = CreateService(watcher: LiveWatcher());
        await service.AddFolderAsync(_folder);
        // Живий watcher може поставити файл у чергу повторно, тож чекаємо на результат, а не на лічильник.
        await WaitUntilAsync(() => service.Progress.IsCompleted && _repository.AllChunks.Count == 1);
        var oldChunkIds = _repository.AllChunks.Select(c => c.Id).ToHashSet();

        await File.WriteAllTextAsync(path, "нова версія тексту");

        await WaitUntilAsync(() =>
            _repository.AllChunks.Count == 1
            && !_repository.AllChunks.Any(c => oldChunkIds.Contains(c.Id))
            && _vectorIndex.Ids.SetEquals(_repository.AllChunks.Select(c => c.Id)));
        Assert.Contains("нова", Assert.Single(_repository.AllChunks).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeletedFile_IsRemovedAfterWatcherEvent()
    {
        var path = Write("note.txt", "текст");
        var service = CreateService(watcher: LiveWatcher());
        await service.AddFolderAsync(_folder);
        // Живий watcher може поставити файл у чергу повторно, тож чекаємо на результат, а не на лічильник.
        await WaitUntilAsync(() => service.Progress.IsCompleted && _repository.AllChunks.Count == 1);

        File.Delete(path);

        await WaitUntilAsync(() => _repository.AllChunks.Count == 0 && _vectorIndex.Count == 0);
        Assert.Empty(await _repository.GetDocumentsAsync());
    }

    [Fact]
    public async Task CorruptedFile_GetsErrorStatusAndDoesNotStopIndexing()
    {
        Write("broken.docx", "це не docx");
        Write("good.txt", "звичайний текст");
        var service = CreateService();

        await service.AddFolderAsync(_folder);
        await WaitUntilAsync(() => service.Progress is { IsCompleted: true, Total: 2 });

        var documents = await _repository.GetDocumentsAsync();
        Assert.Equal(DocumentStatus.Error, documents.Single(d => d.Path.EndsWith("broken.docx", StringComparison.Ordinal)).Status);
        Assert.Equal(DocumentStatus.Indexed, documents.Single(d => d.Path.EndsWith("good.txt", StringComparison.Ordinal)).Status);
        Assert.Equal(1, service.Progress.Failed);
    }

    [Fact]
    public async Task ChunkerOrEmbedderFailure_MarksOnlyThatFileAsError()
    {
        Write("chunker.txt", "збій-нарізки тут");
        Write("embedder.txt", "збій-векторів тут");
        Write("good.txt", "звичайний текст");
        var service = CreateService(
            embedder: new FailingEmbedder(_fakeEmbedder, "збій-векторів"),
            chunker: new FailingChunker(CreateChunker(), "збій-нарізки"));

        await service.AddFolderAsync(_folder);
        await WaitUntilAsync(() => service.Progress is { IsCompleted: true, Total: 3 });

        var documents = (await _repository.GetDocumentsAsync()).ToDictionary(d => Path.GetFileName(d.Path));
        Assert.Equal(DocumentStatus.Error, documents["chunker.txt"].Status);
        Assert.Equal("Нарізка зламалась.", documents["chunker.txt"].Error);
        Assert.Equal(DocumentStatus.Error, documents["embedder.txt"].Status);
        Assert.Equal("Модель не відповідає.", documents["embedder.txt"].Error);
        Assert.Equal(DocumentStatus.Indexed, documents["good.txt"].Status);
        Assert.Equal(2, service.Progress.Failed);
    }

    [Fact]
    public async Task LockedFile_IsRetriedThenMarkedAsError()
    {
        var path = Write("locked.txt", "текст");
        var logger = new ListLogger();
        var service = CreateService(logger: logger, retryDelay: TimeSpan.FromMilliseconds(50));

        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            await service.AddFolderAsync(_folder);
            await WaitUntilAsync(() => service.Progress is { IsCompleted: true, Total: 1 });
        }

        var document = Assert.Single(await _repository.GetDocumentsAsync());
        Assert.Equal(DocumentStatus.Error, document.Status);
        Assert.False(string.IsNullOrWhiteSpace(document.Error));
        Assert.Equal(3, logger.Entries.Count(e => e.Message.Contains("зайнятий", StringComparison.Ordinal)));

        // Коли файл звільнився, повторна перевірка його проіндексує.
        await service.RescanAsync();
        await WaitUntilAsync(() => _repository.AllChunks.Count == 1);
        Assert.Equal(DocumentStatus.Indexed, Assert.Single(await _repository.GetDocumentsAsync()).Status);
    }

    [Fact]
    public async Task LockedFile_ReleasedDuringRetries_IsIndexed()
    {
        var path = Write("locked.txt", "текст");
        var logger = new ListLogger();
        var service = CreateService(logger: logger, retryDelay: TimeSpan.FromSeconds(1));
        var lockStream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        try
        {
            await service.AddFolderAsync(_folder);
            await WaitUntilAsync(() => logger.Entries.Any(e => e.Message.Contains("зайнятий", StringComparison.Ordinal)));
        }
        finally
        {
            await lockStream.DisposeAsync();
        }

        await WaitUntilAsync(() => service.Progress is { IsCompleted: true, Total: 1 });
        Assert.Equal(DocumentStatus.Indexed, Assert.Single(await _repository.GetDocumentsAsync()).Status);
    }

    [Fact]
    public async Task ThrowingProgressHandler_DoesNotStopIndexing()
    {
        Write("a.txt", "кава");
        Write("b.txt", "чай");
        var service = CreateService();
        service.ProgressChanged += (_, _) => throw new InvalidOperationException("Збій обробника.");

        await service.AddFolderAsync(_folder);

        await WaitUntilAsync(() => service.Progress is { IsCompleted: true, Total: 2 });
        Assert.Equal(2, _repository.AllChunks.Count);
    }

    [Fact]
    public async Task RemoveFolder_RemovesDocumentsAndVectors()
    {
        Write("note.txt", "текст");
        var service = CreateService();
        await service.AddFolderAsync(_folder);
        await WaitUntilAsync(() => service.Progress is { IsCompleted: true, Total: 1 });

        await service.RemoveFolderAsync(_folder);

        Assert.Empty(await _repository.GetDocumentsAsync());
        Assert.Equal(0, _vectorIndex.Count);
        Assert.Empty(await service.GetFoldersAsync());
    }

    [Fact]
    public async Task AddFolder_MissingFolder_ThrowsDirectoryNotFoundException()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => service.AddFolderAsync(Path.Combine(_folder, "missing")));
    }

    [Fact]
    public void AddLocalMindCore_RegistersIndexingService()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{StorageOptions.SectionName}:{nameof(StorageOptions.DatabasePath)}"] = Path.Combine(_folder, "test.db"),
            })
            .Build();
        var services = new ServiceCollection()
            .AddLocalMindCore(configuration)
            .AddSingleton<ITokenCounter, FakeTokenCounter>()
            .AddSingleton<IEmbedder>(_fakeEmbedder)
            .AddSingleton<IVectorIndex>(_vectorIndex)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        using var provider = services.BuildServiceProvider();

        Assert.IsType<IndexingService>(provider.GetRequiredService<IIndexingService>());
    }

    private IndexingService CreateService(
        IEmbedder? embedder = null,
        IChunker? chunker = null,
        ILogger<IndexingService>? logger = null,
        TimeSpan? retryDelay = null,
        FolderWatcher? watcher = null)
    {
        _service = new IndexingService(
            _parsers,
            chunker ?? CreateChunker(),
            embedder ?? _fakeEmbedder,
            _repository,
            _vectorIndex,
            new ChangeDetector(_repository),
            watcher ?? _watcher,
            logger ?? NullLogger<IndexingService>.Instance,
            retryDelay);
        return _service;
    }

    /// <summary>Watcher зі звичайним debounce — для тестів, що перевіряють реакцію на зміни файлів.</summary>
    private FolderWatcher LiveWatcher() => _liveWatcher ??= new FolderWatcher(_parsers);

    private static FixedSizeChunker CreateChunker() =>
        new(Options.Create(new ChunkingOptions()), new FakeTokenCounter());

    private string Write(string relativePath, string content)
    {
        var path = Path.Combine(_folder, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>Чекає, поки умова стане істинною, але не довше за таймаут.</summary>
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + _waitTimeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail("Умова не виконалася за відведений час.");
            }

            await Task.Delay(50);
        }
    }

    /// <summary>Векторний індекс у пам'яті — щоб перевірити, що сервіс його оновлює.</summary>
    private sealed class FakeVectorIndex(int dimensions) : IVectorIndex
    {
        private readonly ConcurrentDictionary<Guid, ReadOnlyMemory<float>> _vectors = new();

        public int Dimensions { get; } = dimensions;

        public int Count => _vectors.Count;

        public HashSet<Guid> Ids => _vectors.Keys.ToHashSet();

        public void Add(Guid chunkId, ReadOnlyMemory<float> vector) => _vectors[chunkId] = vector;

        public bool Remove(Guid chunkId) => _vectors.TryRemove(chunkId, out _);

        public IReadOnlyList<ScoredChunk> Search(ReadOnlySpan<float> query, int k) => [];
    }

    /// <summary>Embedder, що «зависає», доки тест не відкриє ворота, — щоб скасувати файл посеред обробки.</summary>
    private sealed class GatedEmbedder(IEmbedder inner) : IEmbedder
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string ModelName => inner.ModelName;

        public int Dimensions => inner.Dimensions;

        public int MaxTokens => inner.MaxTokens;

        public void Open() => _gate.TrySetResult();

        public async Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedAsync(
            IReadOnlyList<string> texts,
            EmbeddingPurpose purpose,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await _gate.Task.WaitAsync(cancellationToken);
            return await inner.EmbedAsync(texts, purpose, cancellationToken);
        }
    }

    /// <summary>Нарізка, що падає на тексті з маркером.</summary>
    private sealed class FailingChunker(IChunker inner, string marker) : IChunker
    {
        public IReadOnlyList<Chunk> Split(Guid documentId, string text) =>
            text.Contains(marker, StringComparison.Ordinal)
                ? throw new InvalidOperationException("Нарізка зламалась.")
                : inner.Split(documentId, text);
    }

    /// <summary>Embedder, що падає, якщо серед текстів є маркер.</summary>
    private sealed class FailingEmbedder(IEmbedder inner, string marker) : IEmbedder
    {
        public string ModelName => inner.ModelName;

        public int Dimensions => inner.Dimensions;

        public int MaxTokens => inner.MaxTokens;

        public Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedAsync(
            IReadOnlyList<string> texts,
            EmbeddingPurpose purpose,
            CancellationToken cancellationToken = default) =>
            texts.Any(t => t.Contains(marker, StringComparison.Ordinal))
                ? throw new InvalidOperationException("Модель не відповідає.")
                : inner.EmbedAsync(texts, purpose, cancellationToken);
    }

    /// <summary>Логер, що запам'ятовує повідомлення, — щоб порахувати повторні спроби.</summary>
    private sealed class ListLogger : ILogger<IndexingService>
    {
        public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Enqueue((logLevel, formatter(state, exception)));
    }
}