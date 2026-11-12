using LocalMind.Core.Ingestion.Parsers;
using System.Collections.Concurrent;
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
    
    public FakeUiDispatcher Dispatcher { get; } = new();

    /// <summary>
    /// Відповіді для наступних викликів <see cref="GetFoldersAsync"/>: дають тесту керувати тим,
    /// коли і чим завершиться оновлення (порядок, збій).
    /// </summary>
    public ConcurrentQueue<TaskCompletionSource<IReadOnlyList<string>>> PendingFolderResults { get; } = new();

    public List<string> AddedFolders { get; } = [];

    public List<string> RemovedFolders { get; } = [];

    public int RescanCount { get; private set; }
    
    public int CancelCount { get; private set; }

    /// <summary>Якщо задано — <see cref="AddFolderAsync"/> завершується цим винятком.</summary>
    public Exception? AddFolderError { get; set; }

    /// <summary>Якщо задано — <see cref="RemoveFolderAsync"/> завершується цим винятком.</summary>
    public Exception? RemoveFolderError { get; set; }

    /// <summary>Якщо задано — <see cref="RescanAsync"/> завершується цим винятком.</summary>
    public Exception? RescanError { get; set; }

    public IndexingProgress Progress { get; private set; } = new(0, 0);

    public bool HasSubscribers => ProgressChanged is not null;

    public event EventHandler<IndexingProgress>? ProgressChanged;

    public Task<IReadOnlyList<string>> GetFoldersAsync(CancellationToken cancellationToken = default) => PendingFolderResults.TryDequeue(out var pending) ? pending.Task : Repository.GetSourceFoldersAsync(cancellationToken);

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
        if (RemoveFolderError is not null)
        {
            return Task.FromException(RemoveFolderError);
        }

        RemovedFolders.Add(path);
        return Repository.RemoveSourceFolderAsync(path, cancellationToken);
    }

    public Task RescanAsync(CancellationToken cancellationToken = default)
    {
        if (RescanError is not null)
        {
            return Task.FromException(RescanError);
        }

        RescanCount++;
        return Task.CompletedTask;
    }

    public void CancelPending() => CancelCount++;

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

        IDocumentParser[] parsers = [new PlainTextParser(), new MarkdownParser(), new PdfParser(), new DocxParser()];
        return new LibraryViewModel(this, Repository, parsers, Picker, Dispatcher, status, NullLogger<LibraryViewModel>.Instance);
    }
    
    public IndexingProgressViewModel CreateProgressViewModel() => new(this, Dispatcher);
}