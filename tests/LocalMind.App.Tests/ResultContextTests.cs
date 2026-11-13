using LocalMind.App.ViewModels;
using LocalMind.App.Controls;
using LocalMind.Core.Models;
using LocalMind.TestUtilities;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LocalMind.App.Tests;

public sealed class ResultContextTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private readonly InMemoryDocumentRepository _repository = new();
    private readonly FakeFileLauncher _files = new();

    [Theory]
    [InlineData(0, 3, 2)]
    [InlineData(1, 3, 3)]
    [InlineData(2, 3, 2)]
    [InlineData(0, 1, 1)]
    public async Task Expand_ShowsAvailableNeighboursAndHighlightsOnlyMatchedChunk(int ordinal, int total, int expectedCount)
    {
        var (document, chunks) = await SaveAsync(total);
        var card = Card(chunks[ordinal], document.Path);
        Assert.False(card.IsContextExpanded);
        Assert.Empty(card.ContextChunks);

        await card.ToggleContextCommand.ExecuteAsync(null);

        Assert.True(card.IsContextExpanded);
        Assert.False(card.IsContextLoading);
        Assert.False(card.HasContextError);
        Assert.Equal(expectedCount, card.ContextChunks.Count);
        Assert.Equal(chunks.Where(c => Math.Abs(c.Ordinal - ordinal) <= 1), card.ContextChunks.Select(c => c.Chunk));
        var match = Assert.Single(card.ContextChunks, c => c.IsMatch);
        Assert.Equal(chunks[ordinal], match.Chunk);
        Assert.Equal(chunks[ordinal].Text, match.Text);
        Assert.Equal("Знайдений фрагмент", match.Title);
        Assert.Equal(ordinal == 0 ? "Знайдений фрагмент" : "Попередній фрагмент", card.ContextChunks[0].Title);
        Assert.Equal(ordinal == total - 1 ? "Знайдений фрагмент" : "Наступний фрагмент", card.ContextChunks[^1].Title);
        Assert.Equal("Менше контексту", card.ContextToggleText);
        await card.OpenSourceCommand.ExecuteAsync(null);
        Assert.Equal(document.Path, Assert.Single(_files.Paths));
    }

    [Fact]
    public async Task CreateAndCollapse_DoNotLoadContext_ReopenLoadsAgain()
    {
        var calls = 0;
        var chunk = TestData.Chunk(Guid.NewGuid());
        _repository.GetChunksByDocumentHandler = (_, _, _, _) =>
        {
            calls++;
            return Task.FromResult<IReadOnlyList<Chunk>>([chunk]);
        };
        var card = Card(chunk);
        Assert.Equal(0, calls);

        await card.ToggleContextCommand.ExecuteAsync(null);
        await card.ToggleContextCommand.ExecuteAsync(null);

        Assert.Equal(1, calls);
        Assert.False(card.IsContextExpanded);
        Assert.Empty(card.ContextChunks);
        Assert.Equal("Більше контексту", card.ContextToggleText);
        await card.ToggleContextCommand.ExecuteAsync(null);
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(5, 4, 6)]
    [InlineData(int.MaxValue, int.MaxValue - 1, int.MaxValue)]
    public async Task Expand_RequestsDocumentAndClampedOrdinalRange(int ordinal, int expectedFrom, int expectedTo)
    {
        var chunk = TestData.Chunk(Guid.NewGuid(), ordinal);
        var request = Completion<(Guid DocumentId, int From, int To)>();
        _repository.GetChunksByDocumentHandler = (document, from, to, _) =>
        {
            request.SetResult((document, from, to));
            return Task.FromResult<IReadOnlyList<Chunk>>([chunk]);
        };
        var card = Card(chunk);

        await card.ToggleContextCommand.ExecuteAsync(null);

        Assert.Equal((chunk.DocumentId, expectedFrom, expectedTo), await request.Task.WaitAsync(Timeout));
        Assert.True(Assert.Single(card.ContextChunks).IsMatch);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Expand_DeletedOrReindexedDocument_ShowsStaleResultMessage(bool reindex)
    {
        var (document, chunks) = await SaveAsync(3);
        var card = Card(chunks[1], document.Path);
        if (reindex)
        {
            await _repository.SaveAsync(document, TestData.Chunks(document.Id, "новий текст"), Vectors(1), "test");
        }
        else
        {
            await _repository.DeleteAsync(document.Id);
        }

        await card.ToggleContextCommand.ExecuteAsync(null);

        Assert.Empty(card.ContextChunks);
        Assert.Equal("Документ змінено або видалено. Виконайте пошук знову.", card.ContextError);
        Assert.True(card.CanRetryContext);
    }

    [Fact]
    public async Task Expand_Failure_ShowsSeparateErrorAndAllowsRetry()
    {
        var (document, chunks) = await SaveAsync(3);
        var card = Card(chunks[1], document.Path);
        _files.Handler = (_, _) => throw new FileNotFoundException();
        await card.OpenSourceCommand.ExecuteAsync(null);
        var fileError = card.ErrorMessage;
        _repository.GetChunksByDocumentHandler = (_, _, _, _) => throw new IOException("database unavailable");

        await card.ToggleContextCommand.ExecuteAsync(null);

        Assert.Equal("Не вдалося завантажити контекст. Спробуйте ще раз.", card.ContextError);
        Assert.Equal(fileError, card.ErrorMessage);
        Assert.True(card.RetryContextCommand.CanExecute(null));
        _repository.GetChunksByDocumentHandler = null;
        await card.RetryContextCommand.ExecuteAsync(null);

        Assert.Equal(3, card.ContextChunks.Count);
        Assert.Null(card.ContextError);
        Assert.False(card.CanRetryContext);
        Assert.Equal(fileError, card.ErrorMessage);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Collapse_IgnoresLateResultOrFailure_AndCancelsRepositoryToken(bool failure)
    {
        var chunk = TestData.Chunk(Guid.NewGuid());
        var started = Completion<CancellationToken>();
        var pending = Completion<IReadOnlyList<Chunk>>();
        _repository.GetChunksByDocumentHandler = (_, _, _, token) =>
        {
            started.SetResult(token);
            // Навмисно ігноруємо токен, щоб перевірити захист від застарілої відповіді.
            return pending.Task.WaitAsync(Timeout, CancellationToken.None);
        };
        var card = Card(chunk);
        var loading = card.ToggleContextCommand.ExecuteAsync(null);
        var token = await started.Task.WaitAsync(Timeout);
        Assert.True(card.IsContextLoading);
        Assert.False(card.CanRetryContext);

        await card.ToggleContextCommand.ExecuteAsync(null);
        Assert.True(token.IsCancellationRequested);
        if (failure)
        {
            pending.SetException(new IOException());
        }
        else
        {
            pending.SetResult([chunk]);
        }

        await loading.WaitAsync(Timeout);
        Assert.False(card.IsContextExpanded);
        Assert.False(card.IsContextLoading);
        Assert.Empty(card.ContextChunks);
        Assert.Null(card.ContextError);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Reopen_OldRequestCannotReplaceNewContextOrClearItsLoadingState(bool oldFinishesFirst)
    {
        var chunk = TestData.Chunk(Guid.NewGuid());
        var firstStarted = Completion();
        var secondStarted = Completion();
        var firstReply = Completion<IReadOnlyList<Chunk>>();
        var secondReply = Completion<IReadOnlyList<Chunk>>();
        var calls = 0;
        _repository.GetChunksByDocumentHandler = (_, _, _, _) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                firstStarted.SetResult();
                return firstReply.Task.WaitAsync(Timeout, CancellationToken.None);
            }

            secondStarted.SetResult();
            return secondReply.Task.WaitAsync(Timeout, CancellationToken.None);
        };
        var card = Card(chunk);
        var first = card.ToggleContextCommand.ExecuteAsync(null);
        await firstStarted.Task.WaitAsync(Timeout);
        await card.ToggleContextCommand.ExecuteAsync(null);
        var second = card.ToggleContextCommand.ExecuteAsync(null);
        await secondStarted.Task.WaitAsync(Timeout);

        if (oldFinishesFirst)
        {
            firstReply.SetResult([]);
            await first.WaitAsync(Timeout);
            Assert.True(card.IsContextLoading);
            Assert.Null(card.ContextError);
        }

        secondReply.SetResult([chunk]);
        await second.WaitAsync(Timeout);
        if (!oldFinishesFirst)
        {
            firstReply.SetException(new IOException());
            await first.WaitAsync(Timeout);
        }

        Assert.Equal(chunk, Assert.Single(card.ContextChunks).Chunk);
        Assert.True(card.IsContextExpanded);
        Assert.False(card.IsContextLoading);
        Assert.Null(card.ContextError);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelCommand_CancelsLoadWithoutShowingError_AndCanReopen(bool ignoresCancellation)
    {
        var started = Completion<CancellationToken>();
        var reply = Completion();
        _repository.GetChunksByDocumentHandler = async (_, _, _, token) =>
        {
            started.SetResult(token);
            if (ignoresCancellation)
            {
                await reply.Task.WaitAsync(Timeout, CancellationToken.None);
            }
            else
            {
                await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
            }

            return [];
        };
        var card = Card(TestData.Chunk(Guid.NewGuid()));
        var loading = card.ToggleContextCommand.ExecuteAsync(null);
        var token = await started.Task.WaitAsync(Timeout);

        card.ToggleContextCommand.Cancel();
        reply.SetResult();
        await loading.WaitAsync(Timeout);

        Assert.True(token.IsCancellationRequested);
        Assert.False(card.IsContextLoading);
        Assert.False(card.IsContextExpanded);
        Assert.Null(card.ContextError);
        _repository.GetChunksByDocumentHandler = null;
        await card.ToggleContextCommand.ExecuteAsync(null);
        Assert.True(card.IsContextExpanded);
    }

    [Theory]
    [InlineData("search")]
    [InlineData("escape")]
    [InlineData("close")]
    public async Task SearchLifecycle_CancelsContextAndIgnoresLateReply(string action)
    {
        var chunk = TestData.Chunk(Guid.NewGuid());
        var search = new FakeSearchService { Handler = (_, _) => Task.FromResult<IReadOnlyList<SearchResult>>([new(chunk, "/notes/context.md", 1)]) };
        var started = Completion<CancellationToken>();
        var reply = Completion<IReadOnlyList<Chunk>>();
        _repository.GetChunksByDocumentHandler = (_, _, _, token) =>
        {
            started.SetResult(token);
            return reply.Task.WaitAsync(Timeout, CancellationToken.None);
        };
        var viewModel = search.CreateViewModel(repository: _repository);
        viewModel.QueryText = "пошук";
        await viewModel.SearchCommand.ExecuteAsync(null);
        var card = Assert.Single(viewModel.Results);
        var loading = card.ToggleContextCommand.ExecuteAsync(null);
        var token = await started.Task.WaitAsync(Timeout);

        switch (action)
        {
            case "search":
                viewModel.QueryText = "новий пошук";
                await viewModel.SearchCommand.ExecuteAsync(null);
                break;
            case "escape":
                viewModel.EscapeCommand.Execute(null);
                break;
            default:
                viewModel.Close();
                break;
        }

        Assert.True(token.IsCancellationRequested);
        reply.SetResult([chunk]);
        await loading.WaitAsync(Timeout);
        Assert.False(card.IsContextExpanded);
        Assert.Empty(card.ContextChunks);
        Assert.Null(card.ContextError);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(200)]
    public async Task Expand_MapsSnippetHighlightsToFullChunk_WithoutAddingDocumentOffset(int startOffset)
    {
        var document = TestData.Document("/notes/context.md");
        const string Text = "🫖 вступ. кава та чай. кінець 🫖";
        var chunk = TestData.Chunk(document.Id, 1, Text, startOffset);
        var previous = TestData.Chunk(document.Id, 0, "кава перед результатом");
        var next = TestData.Chunk(document.Id, 2, "чай після результату");
        await _repository.SaveAsync(document, [previous, chunk, next], Vectors(3), "test");
        var offset = Text.IndexOf("кава", StringComparison.Ordinal);
        var snippet = new Snippet("кава та чай", offset, [new(0, 4), new(8, 3)], hasTextBefore: true, hasTextAfter: true);
        var card = Card(chunk, document.Path, snippet);

        await card.ToggleContextCommand.ExecuteAsync(null);

        var match = Assert.Single(card.ContextChunks, item => item.IsMatch);
        Assert.Equal(Text, match.Snippet.Text);
        Assert.Equal(0, match.Snippet.Offset);
        Assert.False(match.Snippet.HasTextBefore);
        Assert.False(match.Snippet.HasTextAfter);
        Assert.Equal([new HighlightRange(offset, 4), new HighlightRange(offset + 8, 3)], match.Snippet.Highlights);
        var segments = SnippetSegments.Create(match.Snippet);
        Assert.Equal(Text, string.Concat(segments.Select(segment => segment.Text)));
        Assert.Equal(["кава", "чай"], segments.Where(segment => segment.IsHighlighted).Select(segment => segment.Text));
        Assert.All(card.ContextChunks.Where(item => !item.IsMatch), item => Assert.Empty(item.Snippet.Highlights));
        Assert.Same(snippet, card.Snippet);
    }

    [Fact]
    public async Task Expand_WithoutSnippetHighlights_PreservesFullTextWithoutInventingMatches()
    {
        var (document, chunks) = await SaveAsync(3);
        var chunk = chunks[1];
        var snippet = new Snippet(chunk.Text[5..], 5, [], hasTextBefore: true);
        var card = Card(chunk, document.Path, snippet);

        await card.ToggleContextCommand.ExecuteAsync(null);

        var match = Assert.Single(card.ContextChunks, item => item.IsMatch);
        Assert.Equal(chunk.Text, match.Snippet.Text);
        Assert.Empty(match.Snippet.Highlights);
        Assert.All(SnippetSegments.Create(match.Snippet), segment => Assert.False(segment.IsHighlighted));
    }

    [Theory]
    [InlineData("same")]
    [InlineData("id")]
    [InlineData("document")]
    [InlineData("ordinal")]
    [InlineData("offset")]
    [InlineData("text")]
    public async Task Expand_ComparesStoredChunkFields_IndependentlyOfObjectIdentity(string changedField)
    {
        var match = TestData.Chunk(Guid.NewGuid(), 1, "Знайдений текст", 20);
        var stored = new Chunk(
            changedField == "id" ? Guid.NewGuid() : match.Id,
            changedField == "document" ? Guid.NewGuid() : match.DocumentId,
            changedField == "ordinal" ? match.Ordinal + 1 : match.Ordinal,
            changedField == "text" ? "Інший текст" : new string(match.Text.ToCharArray()),
            changedField == "offset" ? match.StartOffset + 1 : match.StartOffset);
        _repository.GetChunksByDocumentHandler = (_, _, _, _) => Task.FromResult<IReadOnlyList<Chunk>>([stored]);
        var card = Card(match);

        await card.ToggleContextCommand.ExecuteAsync(null);

        Assert.NotSame(match, stored);
        if (changedField == "same")
        {
            Assert.Null(card.ContextError);
            Assert.Same(stored, Assert.Single(card.ContextChunks).Chunk);
        }
        else
        {
            Assert.Empty(card.ContextChunks);
            Assert.Equal("Документ змінено або видалено. Виконайте пошук знову.", card.ContextError);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ContextFailure_AlwaysLogsOriginalException_OnlyUpdatesCurrentPanel(bool collapse)
    {
        var logger = new CapturingLogger();
        var started = Completion();
        var pending = Completion<IReadOnlyList<Chunk>>();
        _repository.GetChunksByDocumentHandler = (_, _, _, _) =>
        {
            started.SetResult();
            return pending.Task.WaitAsync(Timeout, CancellationToken.None);
        };
        var card = Card(TestData.Chunk(Guid.NewGuid()), logger: logger);
        var loading = card.ToggleContextCommand.ExecuteAsync(null);
        await started.Task.WaitAsync(Timeout);
        if (collapse)
        {
            card.CloseContext();
        }

        var exception = new SqliteException("database is locked", 5);
        pending.SetException(exception);
        await loading.WaitAsync(Timeout);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Same(exception, entry.Exception);
        Assert.Equal(!collapse, card.HasContextError);
        Assert.Equal(!collapse, card.IsContextExpanded);
        Assert.Empty(card.ContextChunks);
        Assert.False(card.IsContextLoading);
    }

    [Fact]
    public async Task CancelContext_DoesNotLogExpectedCancellation()
    {
        var logger = new CapturingLogger();
        var started = Completion();
        _repository.GetChunksByDocumentHandler = async (_, _, _, token) =>
        {
            started.SetResult();
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
            return [];
        };
        var card = Card(TestData.Chunk(Guid.NewGuid()), logger: logger);
        var loading = card.ToggleContextCommand.ExecuteAsync(null);
        await started.Task.WaitAsync(Timeout);

        card.CloseContext();
        await loading.WaitAsync(Timeout);

        Assert.Empty(logger.Entries);
        Assert.Null(card.ContextError);
    }

    private ResultCardViewModel Card(Chunk chunk, string path = "/notes/context.md", Snippet? snippet = null,
        ILogger<ResultCardViewModel>? logger = null) =>
        new(new SearchResult(chunk, path, 1, snippet), [], _files, logger ?? NullLogger<ResultCardViewModel>.Instance, _repository);

    private async Task<(Document Document, IReadOnlyList<Chunk> Chunks)> SaveAsync(int count)
    {
        var document = TestData.Document("/notes/context.md");
        var chunks = TestData.Chunks(document.Id, [.. Enumerable.Range(0, count).Select(i => $"Повний текст фрагмента {i}")]);
        await _repository.SaveAsync(document, chunks, Vectors(count), "test");
        return (document, chunks);
    }

    private static ReadOnlyMemory<float>[] Vectors(int count) =>
        [.. Enumerable.Range(0, count).Select(_ => (ReadOnlyMemory<float>)new float[] { 1, 0 })];

    private static TaskCompletionSource<T> Completion<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static TaskCompletionSource Completion() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class CapturingLogger : ILogger<ResultCardViewModel>
    {
        public List<(LogLevel Level, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, exception));
    }
}
