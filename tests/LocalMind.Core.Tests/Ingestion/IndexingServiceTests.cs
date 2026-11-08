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
    private static readonly TimeSpan _waitTimeout = TimeSpan.FromSeconds(10);

    private readonly string _folder = Directory.CreateTempSubdirectory("localmind-indexing-").FullName;
    private readonly InMemoryDocumentRepository _repository = new();
    private readonly FakeEmbedder _fakeEmbedder = new();
    private readonly ParserRegistry _parsers = new([new PlainTextParser(), new MarkdownParser(), new DocxParser()]);
    private readonly FakeVectorIndex _vectorIndex;
    private readonly FolderWatcher _watcher;
    private IndexingService? _service;

    public IndexingServiceTests()
    {
        _vectorIndex = new FakeVectorIndex(_fakeEmbedder.Dimensions);
        _watcher = new FolderWatcher(_parsers);
    }

    public void Dispose()
    {
        _service?.Dispose();
        _watcher.Dispose();
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
        await WaitUntilAsync(() => service.Progress is { IsCompleted: true, CurrentFile: null });

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2));
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
        var service = CreateService();
        await service.AddFolderAsync(_folder);
        await WaitUntilAsync(() => service.Progress is { IsCompleted: true, Total: 1 });
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
        var service = CreateService();
        await service.AddFolderAsync(_folder);
        await WaitUntilAsync(() => service.Progress is { IsCompleted: true, Total: 1 });

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

    private IndexingService CreateService(IEmbedder? embedder = null)
    {
        _service = new IndexingService(
            _parsers,
            new FixedSizeChunker(Options.Create(new ChunkingOptions()), new FakeTokenCounter()),
            embedder ?? _fakeEmbedder,
            _repository,
            _vectorIndex,
            new ChangeDetector(_repository),
            _watcher,
            NullLogger<IndexingService>.Instance);
        return _service;
    }

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

        public IReadOnlySet<Guid> Ids => _vectors.Keys.ToHashSet();

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
}