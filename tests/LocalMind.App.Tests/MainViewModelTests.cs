using LocalMind.App.Services;
using LocalMind.App.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

namespace LocalMind.App.Tests;

public sealed class MainViewModelTests
{
    private readonly CoreStatus _status = new(NullLogger<CoreStatus>.Instance);
    private readonly SearchViewModel _search = new FakeSearchService().CreateViewModel();
    private readonly LibraryViewModel _library = new FakeIndexingService().CreateViewModel();
    private readonly SettingsViewModel _settings = new FakeUserSettingsService().CreateViewModel();
    private readonly FakeUiDispatcher _dispatcher = new();
    private readonly UnhandledErrorReporter _errors;
    private readonly IndexingProgressViewModel _indexingProgress = new FakeIndexingService().CreateProgressViewModel();

    public MainViewModelTests() => _errors = new UnhandledErrorReporter(_dispatcher, NullLogger<UnhandledErrorReporter>.Instance);

    [Fact]
    public async Task Dispose_CancelsSearchOwnedByWindow()
    {
        var service = new FakeSearchService();
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Handler = async (_, token) =>
        {
            started.SetResult(token);
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return [];
        };
        var search = service.CreateViewModel();
        using var viewModel = CreateViewModel(search);
        search.QueryText = "нотатка";
        var pending = search.SearchCommand.ExecuteAsync(null);
        var cancellation = await started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        viewModel.Dispose();
        await pending.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(cancellation.IsCancellationRequested);
        Assert.False(search.IsSearching);
        Assert.Null(search.ErrorMessage);
    }

    [Fact]
    public void Dispose_StopsLibraryUpdates()
    {
        var indexing = new FakeIndexingService();
        var viewModel = CreateViewModel(_search, indexing.CreateViewModel());

        viewModel.Dispose();

        Assert.False(indexing.HasSubscribers);
    }

    [Fact]
    public void Dispose_StopsProgressUpdates()
    {
        var indexing = new FakeIndexingService();
        var viewModel = CreateViewModel(progress: indexing.CreateProgressViewModel());

        viewModel.Dispose();

        Assert.False(indexing.HasSubscribers);
    }

    [Fact]
    public void Constructor_SelectsSearch_AndExposesAllPages()
    {
        using var viewModel = CreateViewModel();

        Assert.Same(_search, viewModel.CurrentPage);
        Assert.Same(viewModel.NavigationItems[0], viewModel.SelectedItem);
        Assert.Collection(viewModel.NavigationItems,
            item => Assert.Equal(new NavigationItem("Пошук", _search), item),
            item => Assert.Equal(new NavigationItem("Бібліотека", _library), item),
            item => Assert.Equal(new NavigationItem("Налаштування", _settings), item));
    }

    [Fact]
    public void SelectItem_SwitchesPages_AndReusesTheirViewModels()
    {
        using var viewModel = CreateViewModel();

        foreach (var item in viewModel.NavigationItems.Reverse())
        {
            viewModel.SelectedItem = item;

            Assert.Same(item.Page, viewModel.CurrentPage);
        }

        Assert.Same(_search, viewModel.CurrentPage);
    }

    [Fact]
    public void SelectItem_NotifiesContentBinding_OnlyWhenSelectionChanges()
    {
        using var viewModel = CreateViewModel();
        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        viewModel.SelectedItem = viewModel.NavigationItems[1];

        Assert.Contains(nameof(MainViewModel.SelectedItem), changedProperties);
        Assert.Contains(nameof(MainViewModel.CurrentPage), changedProperties);

        changedProperties.Clear();
        viewModel.SelectedItem = viewModel.NavigationItems[1];

        Assert.Empty(changedProperties);
    }

    [Fact]
    public void Status_WhileStarting_IsStartingEvenIfModelIsMissing()
    {
        _status.SetModelMissing("missing.onnx");
        using var viewModel = CreateViewModel();

        Assert.Equal("Запуск…", viewModel.StatusText);
    }

    [Fact]
    public void Status_ReadyWithModel_IsReady()
    {
        _status.SetModelAvailable();
        _status.SetReady();
        using var viewModel = CreateViewModel();

        Assert.Equal("Готово.", viewModel.StatusText);
        Assert.False(viewModel.HasStatusProblem);
    }

    [Fact]
    public void Status_ReadyWithoutModel_ExplainsMissingModel()
    {
        _status.SetModelMissing("missing.onnx");
        _status.SetReady();
        using var viewModel = CreateViewModel();

        Assert.Equal("Готово, але без моделі.", viewModel.StatusText);
    }

    [Fact]
    public void Status_Failed_ShowsFailureInsteadOfMissingModel()
    {
        _status.SetModelMissing("missing.onnx");
        _status.SetFailed("БД недоступна");
        using var viewModel = CreateViewModel();

        Assert.Equal("Помилка запуску.", viewModel.StatusText);
        Assert.True(viewModel.HasStatusProblem);
    }

    [Fact]
    public void Notice_WhileStartingOrReady_IsHidden()
    {
        using var viewModel = CreateViewModel();

        Assert.False(viewModel.HasNotice);
        Assert.Null(viewModel.NoticeText);

        _status.SetModelAvailable();
        _status.SetReady();

        Assert.False(viewModel.HasNotice);
    }

    [Fact]
    public void Notice_ModelMissing_ExplainsHowToGetModel()
    {
        _status.SetModelMissing("missing.onnx");
        _status.SetReady();
        using var viewModel = CreateViewModel();

        Assert.True(viewModel.HasNotice);
        Assert.Contains("Модель не завантажена", viewModel.NoticeText, StringComparison.Ordinal);
        Assert.Contains("scripts/download-models", viewModel.NoticeText, StringComparison.Ordinal);
    }

    [Fact]
    public void Notice_ModelMissingWhileStarting_IsHiddenUntilReady()
    {
        _status.SetModelMissing("missing.onnx");
        using var viewModel = CreateViewModel();

        Assert.False(viewModel.HasNotice);
    }

    [Fact]
    public void Notice_ModelMissing_WarnsThatIndexingIsUnavailableToo()
    {
        _status.SetModelMissing("missing.onnx");
        _status.SetReady();
        using var viewModel = CreateViewModel();

        Assert.Contains("індексація", viewModel.NoticeText, StringComparison.Ordinal);
    }

    [Fact]
    public void Notice_DatabaseUnavailable_ShowsReasonInsteadOfModelHint()
    {
        _status.SetModelMissing("missing.onnx");
        _status.SetFailed("База даних недоступна: файл пошкоджено");
        using var viewModel = CreateViewModel();

        Assert.True(viewModel.HasNotice);
        Assert.Equal("База даних недоступна: файл пошкоджено", viewModel.NoticeText);
    }

    [Fact]
    public void UnhandledError_ShowsMessageUntilDismissed()
    {
        using var viewModel = CreateViewModel();
        Assert.False(viewModel.HasUnhandledError);

        _errors.Report(new InvalidOperationException("збій"), "тест");
        _dispatcher.RunPending();

        Assert.True(viewModel.HasUnhandledError);
        Assert.Equal(UnhandledErrorReporter.UserMessage, viewModel.UnhandledErrorText);

        viewModel.DismissUnhandledErrorCommand.Execute(null);

        Assert.False(viewModel.HasUnhandledError);
        Assert.Null(viewModel.UnhandledErrorText);
    }

    [Fact]
    public void UnhandledError_AfterDispose_IsIgnored()
    {
        var viewModel = CreateViewModel();
        viewModel.Dispose();

        _errors.Report(new InvalidOperationException("збій"), "тест");
        _dispatcher.RunPending();

        Assert.False(viewModel.HasUnhandledError);
    }
    
    [Fact]
    public async Task DropPaths_OpensLibraryAndAddsFolder()
    {
        var indexing = new FakeIndexingService();
        var library = indexing.CreateViewModel();
        using var viewModel = CreateViewModel(library: library);
        var folder = Directory.CreateTempSubdirectory("localmind-drop-").FullName;

        try
        {
            viewModel.IsDragOver = true;

            await viewModel.DropPathsCommand.ExecuteAsync([folder]);

            Assert.Same(library, viewModel.CurrentPage);
            Assert.False(viewModel.IsDragOver);
            Assert.Equal([folder], indexing.AddedFolders);
        }
        finally
        {
            Directory.Delete(folder);
        }
    }
    private MainViewModel CreateViewModel(
        SearchViewModel? search = null,
        LibraryViewModel? library = null,
        IndexingProgressViewModel? progress = null) =>
        new(_status, _errors, search ?? _search, library ?? _library, _settings, progress ?? _indexingProgress);
}