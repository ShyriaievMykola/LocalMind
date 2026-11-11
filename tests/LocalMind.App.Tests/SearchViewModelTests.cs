using LocalMind.App.Services;
using LocalMind.Core.Errors;
using LocalMind.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace LocalMind.App.Tests;

public sealed class SearchViewModelTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private readonly FakeSearchService _search = new();

    [Fact]
    public void Constructor_UsesConfiguredTopK_AndOffersAllImplementedModes()
    {
        var viewModel = _search.CreateViewModel(defaultTopK: 15);

        Assert.Equal(15m, viewModel.TopK);
        Assert.Equal(SearchMode.Hybrid, viewModel.SelectedMode);
        Assert.Equal([SearchMode.Semantic, SearchMode.Keyword, SearchMode.Hybrid], viewModel.Modes);
        Assert.False(viewModel.IsSearching);
        Assert.Empty(viewModel.Results);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Theory]
    [InlineData(SearchMode.Semantic)]
    [InlineData(SearchMode.Keyword)]
    [InlineData(SearchMode.Hybrid)]
    public async Task Search_PassesQueryModeAndTopK_AndPublishesResults(SearchMode mode)
    {
        IReadOnlyList<SearchResult> results = [Result("перша нотатка", 0.9), Result("друга нотатка", 0.7)];
        _search.Handler = (_, _) => Task.FromResult(results);
        var viewModel = _search.CreateViewModel();
        viewModel.QueryText = "  нотатка  ";
        viewModel.SelectedMode = mode;
        viewModel.TopK = 3;

        await viewModel.SearchCommand.ExecuteAsync(null);

        var call = Assert.Single(_search.Calls);
        Assert.Equal(new SearchQuery("нотатка", mode, 3), call.Query);
        Assert.True(call.Token.CanBeCanceled);
        Assert.Equal(results, viewModel.Results.Select(card => card.Result));
        Assert.False(viewModel.IsSearching);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t ")]
    public async Task Search_EmptyQuery_ShowsValidationWithoutCallingService(string text)
    {
        var viewModel = _search.CreateViewModel();
        viewModel.QueryText = text;

        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.Empty(_search.Calls);
        Assert.Empty(viewModel.Results);
        Assert.Equal("Введіть пошуковий запит.", viewModel.ErrorMessage);
        Assert.False(viewModel.IsSearching);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(1.5d)]
    [InlineData(2147483648d)]
    public async Task Search_InvalidTopK_ShowsValidationWithoutCallingService(double? topK)
    {
        var viewModel = _search.CreateViewModel();
        viewModel.QueryText = "нотатка";
        viewModel.TopK = topK is null ? null : (decimal)topK.Value;

        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.Empty(_search.Calls);
        Assert.True(viewModel.HasError);
        Assert.False(viewModel.IsSearching);
    }

    [Fact]
    public async Task Search_UnknownMode_ShowsValidationWithoutCallingService()
    {
        var viewModel = _search.CreateViewModel();
        viewModel.QueryText = "нотатка";
        viewModel.SelectedMode = (SearchMode)999;

        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.Empty(_search.Calls);
        Assert.Equal("Оберіть режим пошуку.", viewModel.ErrorMessage);
    }

    [Fact]
    public async Task Search_InProgress_ShowsBusyAndClearsPreviousResults()
    {
        _search.Handler = (_, _) => Task.FromResult<IReadOnlyList<SearchResult>>([Result("старий результат")]);
        var viewModel = _search.CreateViewModel();
        viewModel.QueryText = "старий запит";
        await viewModel.SearchCommand.ExecuteAsync(null);
        Assert.Single(viewModel.Results);

        var pending = new TaskCompletionSource<IReadOnlyList<SearchResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _search.Handler = (_, _) => pending.Task.WaitAsync(Timeout, CancellationToken.None);
        viewModel.QueryText = "новий запит";
        var search = viewModel.SearchCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsSearching);
        Assert.Empty(viewModel.Results);
        Assert.Null(viewModel.ErrorMessage);

        pending.SetResult([]);
        await search.WaitAsync(Timeout);

        Assert.False(viewModel.IsSearching);
        Assert.Empty(viewModel.Results);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Search_NewQuery_CancelsPreviousAndIgnoresItsResults(bool previousFinishesFirst)
    {
        var firstStarted = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstResults = new TaskCompletionSource<IReadOnlyList<SearchResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondResults = new TaskCompletionSource<IReadOnlyList<SearchResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        // Сервіс навмисно ігнорує скасування, як може робити вже запущене обчислення моделі.
        _search.Handler = (query, token) =>
        {
            if (query.Text == "перший")
            {
                firstStarted.SetResult(token);
                return firstResults.Task.WaitAsync(Timeout, CancellationToken.None);
            }

            secondStarted.SetResult(token);
            return secondResults.Task.WaitAsync(Timeout, CancellationToken.None);
        };
        var viewModel = _search.CreateViewModel();
        viewModel.QueryText = "перший";
        var first = viewModel.SearchCommand.ExecuteAsync(null);
        var firstToken = await firstStarted.Task.WaitAsync(Timeout);

        viewModel.QueryText = "другий";
        var second = viewModel.SearchCommand.ExecuteAsync(null);
        var secondToken = await secondStarted.Task.WaitAsync(Timeout);

        Assert.True(firstToken.IsCancellationRequested);
        Assert.False(secondToken.IsCancellationRequested);

        if (previousFinishesFirst)
        {
            firstResults.SetResult([Result("застарілий")]);
            await first.WaitAsync(Timeout);
            Assert.True(viewModel.IsSearching);
            Assert.Empty(viewModel.Results);
        }

        var expected = Result("актуальний");
        secondResults.SetResult([expected]);
        await second.WaitAsync(Timeout);

        if (!previousFinishesFirst)
        {
            firstResults.SetResult([Result("застарілий")]);
            await first.WaitAsync(Timeout);
        }

        Assert.Same(expected, Assert.Single(viewModel.Results).Result);
        Assert.False(viewModel.IsSearching);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task Search_StaleFailure_DoesNotReplaceNewResultsOrShowError()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<IReadOnlyList<SearchResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var expected = Result("актуальний");
        _search.Handler = (query, _) =>
        {
            if (query.Text == "перший")
            {
                started.SetResult();
                return pending.Task.WaitAsync(Timeout, CancellationToken.None);
            }

            return Task.FromResult<IReadOnlyList<SearchResult>>([expected]);
        };
        var viewModel = _search.CreateViewModel();
        viewModel.QueryText = "перший";
        var first = viewModel.SearchCommand.ExecuteAsync(null);
        await started.Task.WaitAsync(Timeout);

        viewModel.QueryText = "другий";
        await viewModel.SearchCommand.ExecuteAsync(null);
        pending.SetException(new LocalMindException("Застаріла помилка"));
        await first.WaitAsync(Timeout);

        Assert.Same(expected, Assert.Single(viewModel.Results).Result);
        Assert.Null(viewModel.ErrorMessage);
        Assert.False(viewModel.IsSearching);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Search_CancelOrClose_PropagatesCancellationWithoutError(bool close)
    {
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        _search.Handler = async (_, token) =>
        {
            started.SetResult(token);
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
            return [];
        };
        var viewModel = _search.CreateViewModel();
        viewModel.QueryText = "нотатка";
        var search = viewModel.SearchCommand.ExecuteAsync(null);
        var token = await started.Task.WaitAsync(Timeout);

        if (close)
        {
            viewModel.Close();
        }
        else
        {
            viewModel.SearchCommand.Cancel();
        }

        await search.WaitAsync(Timeout);

        Assert.True(token.IsCancellationRequested);
        Assert.Empty(viewModel.Results);
        Assert.Null(viewModel.ErrorMessage);
        Assert.False(viewModel.IsSearching);
    }

    [Fact]
    public async Task Search_EmptyQueryAfterRunningQuery_CancelsItAndKeepsValidationMessage()
    {
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        _search.Handler = async (_, token) =>
        {
            started.SetResult(token);
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
            return [];
        };
        var viewModel = _search.CreateViewModel();
        viewModel.QueryText = "перший";
        var first = viewModel.SearchCommand.ExecuteAsync(null);
        var token = await started.Task.WaitAsync(Timeout);

        viewModel.QueryText = " ";
        await viewModel.SearchCommand.ExecuteAsync(null);
        await first.WaitAsync(Timeout);

        Assert.True(token.IsCancellationRequested);
        Assert.Single(_search.Calls);
        Assert.Equal("Введіть пошуковий запит.", viewModel.ErrorMessage);
        Assert.Empty(viewModel.Results);
        Assert.False(viewModel.IsSearching);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Search_ServiceFails_ShowsMessageAndAllowsRetry(bool domainError)
    {
        _search.Handler = (_, _) => throw (domainError
            ? new LocalMindException("Модель недоступна.")
            : new InvalidOperationException("Технічна помилка"));
        var viewModel = _search.CreateViewModel();
        viewModel.QueryText = "нотатка";

        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.Equal(domainError ? "Модель недоступна." : "Не вдалося виконати пошук. Спробуйте ще раз.", viewModel.ErrorMessage);
        Assert.Empty(viewModel.Results);
        Assert.False(viewModel.IsSearching);

        _search.Handler = (_, _) => Task.FromResult<IReadOnlyList<SearchResult>>([Result("нотатка")]);
        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.Single(viewModel.Results);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Theory]
    [InlineData(CoreStage.Starting)]
    [InlineData(CoreStage.Failed)]
    public async Task Search_CoreNotReady_ExplainsStateWithoutCallingService(CoreStage stage)
    {
        var status = new CoreStatus(NullLogger<CoreStatus>.Instance);
        if (stage == CoreStage.Failed)
        {
            status.SetFailed("БД недоступна");
        }

        var viewModel = _search.CreateViewModel(status);
        viewModel.QueryText = "нотатка";

        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.Empty(_search.Calls);
        Assert.True(viewModel.HasError);
        Assert.Contains(stage == CoreStage.Failed ? "БД недоступна" : "запускається", viewModel.ErrorMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(SearchMode.Keyword)]
    [InlineData(SearchMode.Semantic)]
    [InlineData(SearchMode.Hybrid)]
    public async Task Search_WithoutModel_AllowsOnlyKeyword(SearchMode mode)
    {
        var status = new CoreStatus(NullLogger<CoreStatus>.Instance);
        status.SetModelMissing("missing.onnx");
        status.SetReady();
        var viewModel = _search.CreateViewModel(status);
        viewModel.QueryText = "нотатка";
        viewModel.SelectedMode = mode;

        await viewModel.SearchCommand.ExecuteAsync(null);

        if (mode == SearchMode.Keyword)
        {
            Assert.Single(_search.Calls);
            Assert.Null(viewModel.ErrorMessage);
        }
        else
        {
            Assert.Empty(_search.Calls);
            Assert.Contains("потрібна модель", viewModel.ErrorMessage, StringComparison.Ordinal);
        }

        Assert.False(viewModel.IsSearching);
    }

    private static SearchResult Result(string text, double score = 0.8) =>
        new(new Chunk(Guid.NewGuid(), Guid.NewGuid(), 0, text, 0), "notes/note.md", score);
}
