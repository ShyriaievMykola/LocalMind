using LocalMind.App.Services;
using LocalMind.App.ViewModels;
using LocalMind.Core.Models;
using LocalMind.TestUtilities;
using Microsoft.Extensions.Logging.Abstractions;

namespace LocalMind.App.Tests;

public sealed class LibraryViewModelTests
{
    private const string Folder = "/notes";
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(10);

    private readonly FakeIndexingService _indexing = new();

    [Fact]
    public async Task Refresh_ShowsFoldersAndDocuments_WithStatusChunksAndError()
    {
        await _indexing.Repository.AddSourceFolderAsync(Folder);
        await SaveAsync("/notes/a.md", DocumentStatus.Indexed, "один", "два");
        await SaveAsync("/notes/broken.pdf", DocumentStatus.Error);
        var viewModel = _indexing.CreateViewModel();

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal([Folder], viewModel.Folders);
        Assert.Equal(2, viewModel.DocumentCount);
        Assert.Equal(1, viewModel.ErrorCount);

        var indexed = viewModel.Documents.Single(d => d.FileName == "a.md");
        Assert.Equal("Проіндексовано", indexed.StatusText);
        Assert.Equal(2, indexed.ChunkCount);
        Assert.False(indexed.IsError);

        var broken = viewModel.Documents.Single(d => d.FileName == "broken.pdf");
        Assert.True(broken.IsError);
        Assert.Equal("Помилка", broken.StatusText);
        Assert.Equal("Тестова помилка", broken.Error);
        Assert.Equal(0, broken.ChunkCount);
    }

    [Fact]
    public async Task Refresh_AfterFailure_SuccessfulRefreshClearsError()
    {
        await _indexing.Repository.AddSourceFolderAsync(Folder);
        var failure = new TaskCompletionSource<IReadOnlyList<string>>();
        failure.SetException(new InvalidOperationException("База недоступна"));
        _indexing.PendingFolderResults.Enqueue(failure);
        var viewModel = _indexing.CreateViewModel();

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal("Не вдалося завантажити бібліотеку.", viewModel.ErrorMessage);

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Null(viewModel.ErrorMessage);
        Assert.Equal([Folder], viewModel.Folders);
    }

    [Fact]
    public async Task Refresh_DoesNotHideErrorOfUserAction()
    {
        _indexing.Picker.Result = Folder;
        _indexing.AddFolderError = new DirectoryNotFoundException();
        var viewModel = _indexing.CreateViewModel();
        await viewModel.AddFolderCommand.ExecuteAsync(null);

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal($"Папку не знайдено: {Folder}", viewModel.ErrorMessage);
    }

    [Fact]
    public async Task Refresh_OlderResultFinishingLater_DoesNotOverwriteNewer()
    {
        var older = new TaskCompletionSource<IReadOnlyList<string>>();
        var newer = new TaskCompletionSource<IReadOnlyList<string>>();
        _indexing.PendingFolderResults.Enqueue(older);
        _indexing.PendingFolderResults.Enqueue(newer);
        var viewModel = _indexing.CreateViewModel();

        var first = viewModel.RefreshCommand.ExecuteAsync(null);
        var second = viewModel.RefreshCommand.ExecuteAsync(null);

        newer.SetResult(["/new"]);
        await second;
        older.SetResult(["/old"]);
        await first;

        Assert.Equal(["/new"], viewModel.Folders);
    }

    [Fact]
    public async Task Refresh_KeepsSelectedFolder_EvenIfListResetsSelection()
    {
        await _indexing.Repository.AddSourceFolderAsync(Folder);
        var viewModel = _indexing.CreateViewModel();
        await viewModel.RefreshCommand.ExecuteAsync(null);
        viewModel.SelectedFolder = Folder;

        // Так поводиться ListBox: новий ItemsSource скидає вибір, і TwoWay-прив'язка пише null у VM.
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(LibraryViewModel.Folders))
            {
                viewModel.SelectedFolder = null;
            }
        };

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(Folder, viewModel.SelectedFolder);
        Assert.True(viewModel.RequestRemoveFolderCommand.CanExecute(null));
    }

    [Fact]
    public async Task Refresh_WhileCoreStarting_DoesNotReadStorage_ThenLoadsWhenReady()
    {
        await _indexing.Repository.AddSourceFolderAsync(Folder);
        await SaveAsync("/notes/a.md", DocumentStatus.Indexed, "текст");
        var status = new CoreStatus(NullLogger<CoreStatus>.Instance);
        var viewModel = _indexing.CreateViewModel(status);

        await viewModel.RefreshCommand.ExecuteAsync(null);
        Assert.Empty(viewModel.Documents);

        status.SetModelAvailable();
        status.SetReady();
        _indexing.Dispatcher.RunPending();

        await WaitUntilAsync(() => viewModel.DocumentCount == 1);
    }

    [Fact]
    public async Task ProgressFromBackgroundThread_IsAppliedOnlyThroughUiDispatcher()
    {
        var viewModel = _indexing.CreateViewModel();

        await Task.Run(() => _indexing.Report(new IndexingProgress(0, 1, 0, "/notes/a.md")));

        // Обробник фонової події лише поставив дію в чергу UI-потоку і нічого не змінив сам.
        Assert.False(viewModel.IsIndexing);
        Assert.Equal(1, _indexing.Dispatcher.PendingCount);

        _indexing.Dispatcher.RunPending();

        Assert.True(viewModel.IsIndexing);
        Assert.Equal("/notes/a.md", viewModel.Progress?.CurrentFile);
    }

    [Fact]
    public async Task IndexingProgress_IsShown_AndCompletionRefreshesDocuments()
    {
        await _indexing.Repository.AddSourceFolderAsync(Folder);
        var viewModel = _indexing.CreateViewModel();
        await viewModel.RefreshCommand.ExecuteAsync(null);

        _indexing.Report(new IndexingProgress(0, 1, 0, "/notes/a.md"));
        _indexing.Dispatcher.RunPending();

        Assert.True(viewModel.IsIndexing);

        await SaveAsync("/notes/a.md", DocumentStatus.Indexed, "текст");
        _indexing.Report(new IndexingProgress(1, 1));
        _indexing.Dispatcher.RunPending();

        Assert.False(viewModel.IsIndexing);
        await WaitUntilAsync(() => viewModel.DocumentCount == 1);
    }

    [Fact]
    public async Task AddFolder_PicksFolderAndStartsIndexing_ThenShowsIt()
    {
        _indexing.Picker.Result = Folder;
        var viewModel = _indexing.CreateViewModel();

        await viewModel.AddFolderCommand.ExecuteAsync(null);

        Assert.Equal([Folder], _indexing.AddedFolders);
        Assert.Equal([Folder], viewModel.Folders);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task AddFolder_PickerCancelled_DoesNothing()
    {
        var viewModel = _indexing.CreateViewModel();

        await viewModel.AddFolderCommand.ExecuteAsync(null);

        Assert.Equal(1, _indexing.Picker.Calls);
        Assert.Empty(_indexing.AddedFolders);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task AddFolder_WithoutModel_ExplainsAndDoesNotOpenPicker()
    {
        var status = new CoreStatus(NullLogger<CoreStatus>.Instance);
        status.SetModelMissing("missing.onnx");
        status.SetReady();
        var viewModel = _indexing.CreateViewModel(status);

        await viewModel.AddFolderCommand.ExecuteAsync(null);

        Assert.Equal(0, _indexing.Picker.Calls);
        Assert.Contains("модель", viewModel.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddFolder_WhileCoreStarting_ShowsMessage()
    {
        var viewModel = _indexing.CreateViewModel(new CoreStatus(NullLogger<CoreStatus>.Instance));

        await viewModel.AddFolderCommand.ExecuteAsync(null);

        Assert.Equal(0, _indexing.Picker.Calls);
        Assert.Equal("Ядро ще запускається. Спробуйте за мить.", viewModel.ErrorMessage);
    }

    [Fact]
    public async Task AddFolder_FolderDisappeared_ShowsError()
    {
        _indexing.Picker.Result = Folder;
        _indexing.AddFolderError = new DirectoryNotFoundException();
        var viewModel = _indexing.CreateViewModel();

        await viewModel.AddFolderCommand.ExecuteAsync(null);

        Assert.Equal($"Папку не знайдено: {Folder}", viewModel.ErrorMessage);
        Assert.Empty(viewModel.Folders);
    }

    [Fact]
    public async Task RemoveFolder_AsksForConfirmation_AndRemovesOnlyAfterIt()
    {
        await _indexing.Repository.AddSourceFolderAsync(Folder);
        await SaveAsync("/notes/a.md", DocumentStatus.Indexed, "текст");
        var viewModel = _indexing.CreateViewModel();
        await viewModel.RefreshCommand.ExecuteAsync(null);
        viewModel.SelectedFolder = Folder;

        viewModel.RequestRemoveFolderCommand.Execute(null);

        Assert.True(viewModel.IsConfirmingRemoval);
        Assert.Equal(Folder, viewModel.FolderPendingRemoval);
        Assert.Empty(_indexing.RemovedFolders);

        viewModel.CancelRemoveFolderCommand.Execute(null);

        Assert.False(viewModel.IsConfirmingRemoval);
        Assert.Empty(_indexing.RemovedFolders);

        viewModel.RequestRemoveFolderCommand.Execute(null);
        await viewModel.ConfirmRemoveFolderCommand.ExecuteAsync(null);

        Assert.Equal([Folder], _indexing.RemovedFolders);
        Assert.False(viewModel.IsConfirmingRemoval);
        Assert.Empty(viewModel.Folders);
        Assert.Empty(viewModel.Documents);
    }

    [Fact]
    public void RequestRemoveFolder_WithoutSelection_IsDisabled()
    {
        var viewModel = _indexing.CreateViewModel();

        Assert.False(viewModel.RequestRemoveFolderCommand.CanExecute(null));

        viewModel.SelectedFolder = Folder;

        Assert.True(viewModel.RequestRemoveFolderCommand.CanExecute(null));
    }

    [Fact]
    public async Task Rescan_CallsIndexingService()
    {
        var viewModel = _indexing.CreateViewModel();

        await viewModel.RescanCommand.ExecuteAsync(null);

        Assert.Equal(1, _indexing.RescanCount);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public void Close_UnsubscribesFromIndexingEvents()
    {
        var viewModel = _indexing.CreateViewModel();

        viewModel.Close();

        Assert.False(_indexing.HasSubscribers);
    }

    private async Task SaveAsync(string path, DocumentStatus status, params string[] chunkTexts)
    {
        var document = TestData.Document(path, status);
        var chunks = TestData.Chunks(document.Id, chunkTexts);
        IReadOnlyList<ReadOnlyMemory<float>> vectors = [.. chunks.Select(_ => new ReadOnlyMemory<float>([1f]))];
        await _indexing.Repository.SaveAsync(document, chunks, vectors, "test-model");
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + WaitTimeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail("Умова не виконалася за відведений час.");
            }

            await Task.Delay(20);
        }
    }
}
