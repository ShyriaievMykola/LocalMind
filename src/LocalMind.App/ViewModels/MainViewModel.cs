using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using LocalMind.App.Services;

namespace LocalMind.App.ViewModels;

/// <summary>Навігація головного вікна і стан запуску ядра.</summary>
public sealed partial class MainViewModel : ViewModelBase, IDisposable
{
    private readonly CoreStatus _status;
    private readonly SearchViewModel _search;
    private readonly LibraryViewModel _library;
    private bool _disposed;

    public MainViewModel(
        CoreStatus status,
        SearchViewModel search,
        LibraryViewModel library,
        SettingsViewModel settings,
        IndexingProgressViewModel indexing)
    {
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(search);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(indexing);

        _search = search;
        _library = library;
        Indexing = indexing;

        NavigationItems = Array.AsReadOnly<NavigationItem>([
            new("Пошук", search),
            new("Бібліотека", library),
            new("Налаштування", settings),
        ]);
        SelectedItem = NavigationItems[0];

        _status = status;
        _status.Changed += OnStatusChanged;
        UpdateStatus();
    }

    /// <summary>Прогрес індексації для рядка стану.</summary>
    public IndexingProgressViewModel Indexing { get; }
    public IReadOnlyList<NavigationItem> NavigationItems { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentPage))]
    public partial NavigationItem SelectedItem { get; set; }

    public ViewModelBase CurrentPage => SelectedItem.Page;

    [ObservableProperty]
    public partial string StatusText { get; private set; } = string.Empty;
    
    /// <summary>Ядро не готове або працює без моделі — такий стан видно навіть під час індексації.</summary>
    [ObservableProperty]
    public partial bool HasStatusProblem { get; private set; }

    public void Dispose()
    {
        _disposed = true;
        _status.Changed -= OnStatusChanged;
        _search.Close();
        _library.Close();
        Indexing.Close();
    }

    private void OnStatusChanged(object? sender, EventArgs args) => Dispatcher.UIThread.Post(() =>
    {
        if (!_disposed)
        {
            UpdateStatus();
        }
    });

    private void UpdateStatus()
    {
        StatusText = _status.Stage switch
        {
            CoreStage.Starting => "Запуск…",
            CoreStage.Failed => $"Помилка запуску. {_status.Error}",
            _ when !_status.IsModelAvailable => $"Готово, але без моделі. {_status.ModelError}",
            _ => "Готово.",
        };
        HasStatusProblem = _status.Stage != CoreStage.Ready || !_status.IsModelAvailable;
    }
}
