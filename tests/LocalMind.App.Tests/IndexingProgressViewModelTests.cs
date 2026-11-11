using LocalMind.Core.Models;

namespace LocalMind.App.Tests;

public sealed class IndexingProgressViewModelTests
{
    private readonly FakeIndexingService _indexing = new();

    [Fact]
    public void Constructor_NothingIndexing_HidesProgressAndDisablesCancel()
    {
        var viewModel = _indexing.CreateProgressViewModel();

        Assert.False(viewModel.IsIndexing);
        Assert.Null(viewModel.ProgressText);
        Assert.False(viewModel.CancelCommand.CanExecute(null));
    }

    [Fact]
    public void Constructor_IndexingAlreadyRunning_ShowsProgressImmediately()
    {
        _indexing.Report(new IndexingProgress(3, 10, 0, "/notes/a.md"));

        var viewModel = _indexing.CreateProgressViewModel();

        Assert.True(viewModel.IsIndexing);
        Assert.Equal("Індексація: 3 з 10", viewModel.ProgressText);
    }

    [Fact]
    public async Task ProgressFromBackgroundThread_UpdatesPropertiesOnlyThroughUiDispatcher()
    {
        var viewModel = _indexing.CreateProgressViewModel();

        await Task.Run(() => _indexing.Report(new IndexingProgress(2, 10, 1, "/notes/sub/a.md")));

        // Обробник фонової події лише поставив дію в чергу UI-потоку і нічого не змінив сам.
        Assert.False(viewModel.IsIndexing);
        Assert.Equal(1, _indexing.Dispatcher.PendingCount);

        _indexing.Dispatcher.RunPending();

        Assert.True(viewModel.IsIndexing);
        Assert.Equal("Індексація: 2 з 10, помилок: 1", viewModel.ProgressText);
        Assert.Equal(0.2, viewModel.Fraction, precision: 3);
        Assert.Equal("a.md", viewModel.CurrentFileName);
        Assert.True(viewModel.CancelCommand.CanExecute(null));
    }

    [Fact]
    public void Cancel_CallsService_ShowsCancelling_UntilIndexingStops()
    {
        _indexing.Report(new IndexingProgress(1, 5, 0, "/notes/a.md"));
        var viewModel = _indexing.CreateProgressViewModel();

        viewModel.CancelCommand.Execute(null);

        Assert.Equal(1, _indexing.CancelCount);
        Assert.True(viewModel.IsCancelling);
        Assert.Equal("Скасовую…", viewModel.CancelText);
        Assert.False(viewModel.CancelCommand.CanExecute(null));

        // Поточний файл завершився — черга порожня.
        _indexing.Report(new IndexingProgress(2, 2));
        _indexing.Dispatcher.RunPending();

        Assert.False(viewModel.IsIndexing);
        Assert.False(viewModel.IsCancelling);
        Assert.Equal("Скасувати", viewModel.CancelText);
    }

        [Fact]
    public void Cancel_ThenNewWorkArrives_EnablesCancelAgain()
    {
        _indexing.Report(new IndexingProgress(1, 5, 0, "/notes/a.md"));
        var viewModel = _indexing.CreateProgressViewModel();
        viewModel.CancelCommand.Execute(null);

        // Поки поточний файл завершується, watcher ставить у чергу новий файл: всього стало більше.
        _indexing.Report(new IndexingProgress(1, 6, 0, "/notes/a.md"));
        _indexing.Dispatcher.RunPending();

        Assert.True(viewModel.IsIndexing);
        Assert.False(viewModel.IsCancelling);
        Assert.True(viewModel.CancelCommand.CanExecute(null));
    }

    [Fact]
    public void EventQueuedBeforeClose_IsIgnoredAfterClose()
    {
        var viewModel = _indexing.CreateProgressViewModel();
        _indexing.Report(new IndexingProgress(0, 3, 0, "/notes/a.md"));

        viewModel.Close();
        _indexing.Dispatcher.RunPending();

        Assert.False(viewModel.IsIndexing);
    }

    [Fact]
    public void Close_UnsubscribesFromIndexingEvents()
    {
        var viewModel = _indexing.CreateProgressViewModel();

        viewModel.Close();

        Assert.False(_indexing.HasSubscribers);
    }

    [Fact]
    public void Idle_ExposesEmptyDefaults()
    {
        var viewModel = _indexing.CreateProgressViewModel();

        Assert.True(viewModel.IsIdle);
        Assert.Equal(0, viewModel.Fraction);
        Assert.Null(viewModel.CurrentFileName);
        Assert.Equal("Скасувати", viewModel.CancelText);
    }

    [Fact]
    public void Progress_WithoutCurrentFile_ShowsCountsOnly()
    {
        _indexing.Report(new IndexingProgress(1, 3));

        var viewModel = _indexing.CreateProgressViewModel();

        Assert.False(viewModel.IsIdle);
        Assert.Equal("Індексація: 1 з 3", viewModel.ProgressText);
        Assert.Null(viewModel.CurrentFileName);
    }

    [Fact]
    public void Cancel_ThenProgressAdvancesOnSameWork_StaysCancelling()
    {
        _indexing.Report(new IndexingProgress(1, 5, 0, "/notes/a.md"));
        var viewModel = _indexing.CreateProgressViewModel();
        viewModel.CancelCommand.Execute(null);

        _indexing.Report(new IndexingProgress(2, 5, 0, "/notes/b.md"));
        _indexing.Dispatcher.RunPending();

        Assert.True(viewModel.IsIndexing);
        Assert.True(viewModel.IsCancelling);
        Assert.False(viewModel.CancelCommand.CanExecute(null));
    }

    [Fact]
    public void Cancel_ThenProgressRestartsFromZero_EnablesCancelAgain()
    {
        _indexing.Report(new IndexingProgress(3, 5, 0, "/notes/a.md"));
        var viewModel = _indexing.CreateProgressViewModel();
        viewModel.CancelCommand.Execute(null);

        _indexing.Report(new IndexingProgress(0, 5, 0, "/notes/new.md"));
        _indexing.Dispatcher.RunPending();

        Assert.False(viewModel.IsCancelling);
        Assert.True(viewModel.CancelCommand.CanExecute(null));
    }
}
