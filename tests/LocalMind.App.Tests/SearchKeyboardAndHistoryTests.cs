using LocalMind.Core.Models;

namespace LocalMind.App.Tests;

public sealed class SearchKeyboardAndHistoryTests
{
    private readonly FakeSearchService _search = new();

    [Fact]
    public async Task Search_AddsQueryToHistory_AndClosesSuggestions()
    {
        var viewModel = _search.CreateViewModel();
        viewModel.QueryText = "docker мережа";

        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.Equal(["docker мережа"], _search.History.Items);
        Assert.False(viewModel.HasSuggestions);
    }

    [Fact]
    public void Typing_ShowsMatchingQueriesFromHistory()
    {
        _search.History.Entries.AddRange(["Docker compose", "кава", "docker мережа"]);
        var viewModel = _search.CreateViewModel();

        viewModel.QueryText = "dock";

        Assert.True(viewModel.HasSuggestions);
        Assert.Equal(["Docker compose", "docker мережа"], viewModel.Suggestions);
    }

    [Fact]
    public async Task ArrowDownAndEnter_OnSuggestion_SearchesIt()
    {
        _search.History.Entries.AddRange(["кава зранку", "чай"]);
        var viewModel = _search.CreateViewModel();
        viewModel.RequestFocus();

        viewModel.MoveDownCommand.Execute(null);
        await viewModel.SubmitCommand.ExecuteAsync(null);

        Assert.Equal("кава зранку", viewModel.QueryText);
        Assert.Equal("кава зранку", Assert.Single(_search.Calls).Query.Text);
        Assert.False(viewModel.HasSuggestions);
    }

    [Fact]
    public async Task ArrowsSelectResults_EnterOpensSelectedSource()
    {
        IReadOnlyList<SearchResult> found = [Result("notes/a.md"), Result("notes/b.md")];
        _search.Handler = (_, _) => Task.FromResult(found);
        var viewModel = _search.CreateViewModel();
        viewModel.QueryText = "нотатки";
        await viewModel.SearchCommand.ExecuteAsync(null);

        viewModel.MoveDownCommand.Execute(null);
        viewModel.MoveDownCommand.Execute(null);
        await viewModel.SubmitCommand.ExecuteAsync(null);

        Assert.Equal("notes/b.md", Assert.Single(_search.Files.Paths));
        Assert.Single(_search.Calls);
    }

    [Fact]
    public async Task TypingAfterSelectingResult_EnterSearchesAgain()
    {
        IReadOnlyList<SearchResult> found = [Result("notes/a.md")];
        _search.Handler = (_, _) => Task.FromResult(found);
        var viewModel = _search.CreateViewModel();
        viewModel.QueryText = "нотатки";
        await viewModel.SearchCommand.ExecuteAsync(null);
        viewModel.MoveDownCommand.Execute(null);

        viewModel.QueryText = "інший запит";
        await viewModel.SubmitCommand.ExecuteAsync(null);

        Assert.Equal(2, _search.Calls.Count);
        Assert.Empty(_search.Files.Paths);
    }

    [Fact]
    public async Task Escape_ClosesSuggestionsFirst_ThenClearsQueryAndResults()
    {
        _search.History.Entries.Add("кава зранку");
        IReadOnlyList<SearchResult> found = [Result("notes/a.md")];
        _search.Handler = (_, _) => Task.FromResult(found);
        var viewModel = _search.CreateViewModel();
        viewModel.QueryText = "кава";
        await viewModel.SearchCommand.ExecuteAsync(null);
        viewModel.QueryText = "кав";
        Assert.True(viewModel.HasSuggestions);

        viewModel.EscapeCommand.Execute(null);

        Assert.False(viewModel.HasSuggestions);
        Assert.Equal("кав", viewModel.QueryText);
        Assert.Single(viewModel.Results);

        viewModel.EscapeCommand.Execute(null);

        Assert.Equal(string.Empty, viewModel.QueryText);
        Assert.Empty(viewModel.Results);
    }

    [Fact]
    public async Task ClearHistory_RemovesSuggestions()
    {
        _search.History.Entries.Add("кава");
        var viewModel = _search.CreateViewModel();
        viewModel.RequestFocus();
        Assert.True(viewModel.HasSuggestions);

        await viewModel.ClearHistoryCommand.ExecuteAsync(null);

        Assert.Empty(_search.History.Items);
        Assert.False(viewModel.HasSuggestions);
    }

    [Fact]
    public void ShowSuggestions_WithEmptyQuery_ShowsRecentHistory()
    {
        _search.History.Entries.AddRange(["чай", "кава"]);
        var viewModel = _search.CreateViewModel();
        Assert.False(viewModel.HasSuggestions);

        viewModel.ShowSuggestions();

        Assert.True(viewModel.HasSuggestions);
        Assert.Equal(["чай", "кава"], viewModel.Suggestions);
    }
    
    [Fact]
    public async Task FocusAfterSearch_ArrowsStillSelectResults()
    {
        _search.History.Entries.Add("нотатки старі");
        IReadOnlyList<SearchResult> found = [Result("notes/a.md")];
        _search.Handler = (_, _) => Task.FromResult(found);
        var viewModel = _search.CreateViewModel();
        viewModel.QueryText = "нотатки";
        await viewModel.SearchCommand.ExecuteAsync(null);

        viewModel.ShowSuggestions();
        viewModel.MoveDownCommand.Execute(null);

        Assert.False(viewModel.HasSuggestions);
        Assert.Same(viewModel.Results[0], viewModel.SelectedResult);
    }

    [Fact]
    public async Task FailedSearch_IsNotAddedToHistory()
    {
        _search.Handler = (_, _) =>
            Task.FromException<IReadOnlyList<SearchResult>>(new InvalidOperationException("пошук упав"));
        var viewModel = _search.CreateViewModel();
        viewModel.QueryText = "кава";

        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasError);
        Assert.Empty(_search.History.Items);
    }

    [Fact]
    public void RequestFocus_IsTakenOnlyOnce()
    {
        var viewModel = _search.CreateViewModel();

        viewModel.RequestFocus();

        Assert.True(viewModel.TakeFocusRequest());
        Assert.False(viewModel.TakeFocusRequest());
    }

    private static SearchResult Result(string path) =>
        new(new Chunk(Guid.NewGuid(), Guid.NewGuid(), 0, "текст", 0), path, 0.9);
}