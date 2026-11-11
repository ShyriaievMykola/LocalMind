using LocalMind.App.Services;
using LocalMind.App.ViewModels;
using LocalMind.Core.Abstractions;
using LocalMind.Core.Models;
using LocalMind.TestUtilities;
using Microsoft.Extensions.Logging.Abstractions;

namespace LocalMind.App.Tests;

/// <summary>Індексація без файлової системи: папки зберігаються в <see cref="InMemoryDocumentRepository"/>.</summary>
internal sealed class FakeIndexingService : IIndexingService
{
    public InMemoryDocumentRepository Repository { get; } = new();

    public FakeFolderPicker Picker { get; } = new();

    public List<string> AddedFolders { get; } = [];

    public List<string> RemovedFolders { get; } = [];

    public int RescanCount { get; private set; }

    /// <summary>Якщо задано — <see cref="AddFolderAsync"/> завершується цим винятком.</summary>
    public Exception? AddFolderError { get; set; }

    public IndexingProgress Progress { get; private set; } = new(0, 0);

    public bool HasSubscribers => ProgressChanged is not null;

    public event EventHandler<IndexingProgress>? ProgressChanged;

    public Task<IReadOnlyList<string>> GetFoldersAsync(CancellationToken cancellationToken = default) =>
        Repository.GetSourceFoldersAsync(cancellationToken);

    public Task AddFolderAsync(string path, CancellationToken cancellationToken = default)
    {
        if (AddFolderError is not null)
        {
            return Task.FromException(AddFolderError);
        }

        AddedFolders.Add(path);
        return Repository.AddSourceFolderAsync(path, cancellationToken);
    }

    public Task RemoveFolderAsync(string path, CancellationToken cancellationToken = default)
    {
        RemovedFolders.Add(path);
        return Repository.RemoveSourceFolderAsync(path, cancellationToken);
    }

    public Task RescanAsync(CancellationToken cancellationToken = default)
    {
        RescanCount++;
        return Task.CompletedTask;
    }

    public void CancelPending()
    {
    }

    /// <summary>Імітує подію прогресу від справжнього сервісу.</summary>
    public void Report(IndexingProgress progress)
    {
        Progress = progress;
        ProgressChanged?.Invoke(this, progress);
    }

    public LibraryViewModel CreateViewModel(CoreStatus? status = null)
    {
        if (status is null)
        {
            status = new CoreStatus(NullLogger<CoreStatus>.Instance);
            status.SetModelAvailable();
            status.SetReady();
        }

        return new LibraryViewModel(this, Repository, Picker, status, NullLogger<LibraryViewModel>.Instance);
    }
}