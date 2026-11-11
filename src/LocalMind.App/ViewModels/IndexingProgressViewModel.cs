using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalMind.App.Services;
using LocalMind.Core.Abstractions;
using LocalMind.Core.Models;

namespace LocalMind.App.ViewModels;

/// <summary>
/// Прогрес фонової індексації для рядка стану головного вікна — видно з будь-якої сторінки.
/// Події приходять з фонового потоку і переносяться в UI-потік через <see cref="IUiDispatcher"/>.
/// </summary>
public sealed partial class IndexingProgressViewModel : ViewModelBase
{
    private readonly IIndexingService _indexing;
    private readonly IUiDispatcher _dispatcher;
    private readonly EventHandler<IndexingProgress> _progressHandler;
    private volatile bool _closed;

    public IndexingProgressViewModel(IIndexingService indexing, IUiDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(indexing);
        ArgumentNullException.ThrowIfNull(dispatcher);

        _indexing = indexing;
        _dispatcher = dispatcher;
        _progressHandler = (_, _) => _dispatcher.Post(() =>
        {
            if (!_closed)
            {
                ShowProgress();
            }
        });
        _indexing.ProgressChanged += _progressHandler;

        // Індексація могла початися ще до відкриття вікна (перевірка папок під час запуску).
        ShowProgress();
    }

    /// <summary>Поточний прогрес; <see langword="null"/>, коли черга порожня.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIndexing), nameof(ProgressText), nameof(Fraction), nameof(CurrentFileName))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    public partial IndexingProgress? Progress { get; private set; }

    public bool IsIndexing => Progress is not null;

    public double Fraction => Progress?.Fraction ?? 0;

    public string? ProgressText => Progress switch
    {
        null => null,
        { Failed: > 0 } progress => $"Індексація: {progress.Processed} з {progress.Total}, помилок: {progress.Failed}",
        { } progress => $"Індексація: {progress.Processed} з {progress.Total}",
    };

    /// <summary>Лише назва файла; повний шлях — у підказці.</summary>
    public string? CurrentFileName => Progress?.CurrentFile is { } path ? Path.GetFileName(path) : null;

    /// <summary>Скасування запитано, поточний файл ще завершується.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CancelText))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    public partial bool IsCancelling { get; private set; }

    public string CancelText => IsCancelling ? "Скасовую…" : "Скасувати";

    /// <summary>Відписується від подій ядра при закритті вікна.</summary>
    public void Close()
    {
        _closed = true;
        _indexing.ProgressChanged -= _progressHandler;
    }

    /// <summary>Очищує чергу і зупиняє поточний файл; уже проіндексоване лишається (AC-11).</summary>
    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        IsCancelling = true;
        _indexing.CancelPending();
    }

    private bool CanCancel() => IsIndexing && !IsCancelling;

    /// <summary>Читає поточний прогрес із сервісу, а не з аргументу події: події з різних потоків можуть прийти не по черзі.</summary>
    private void ShowProgress()
    {
        var progress = _indexing.Progress;
        Progress = progress.IsCompleted ? null : progress;
        if (Progress is null)
        {
            IsCancelling = false;
        }
    }
}