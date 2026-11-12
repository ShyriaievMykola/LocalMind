using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalMind.App.Services;

namespace LocalMind.App.ViewModels;

/// <summary>Навігація головного вікна, стан запуску ядра і повідомлення про необроблені помилки.</summary>
public sealed partial class MainViewModel : ViewModelBase, IDisposable
{
    private const string ModelMissingNotice =
        "Модель не завантажена — запустіть scripts/download-models. Без неї не працюють семантичний пошук та індексація; доступний лише пошук Keyword.";

    private readonly CoreStatus _status;
    private readonly UnhandledErrorReporter _errors;
    private readonly SearchViewModel _search;
    private readonly LibraryViewModel _library;
    private bool _disposed;

    public MainViewModel(
        CoreStatus status,
        UnhandledErrorReporter errors,
        SearchViewModel search,
        LibraryViewModel library,
        SettingsViewModel settings,
        IndexingProgressViewModel indexing)
    {
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(errors);
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
        _errors = errors;
        _errors.Reported += OnErrorReported;
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

    /// <summary>Що не так із ядром: база недоступна або немає моделі; <see langword="null"/>, коли все гаразд.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    public partial string? NoticeText { get; private set; }

    public bool HasNotice => NoticeText is not null;

    /// <summary>Повідомлення про необроблений виняток; лишається, поки користувач його не закриє.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnhandledError))]
    public partial string? UnhandledErrorText { get; private set; }

    public bool HasUnhandledError => UnhandledErrorText is not null;

    [RelayCommand]
    private void DismissUnhandledError() => UnhandledErrorText = null;

    /// <summary>Над вікном тягнуть файли — показуємо підказку «відпустіть, щоб додати».</summary>
    [ObservableProperty]
    public partial bool IsDragOver { get; set; }

    /// <summary>Перетягнуті у вікно файли й папки: відкриваємо «Бібліотеку» — там видно результат і пояснення.</summary>
    [RelayCommand]
    private async Task DropPathsAsync(IReadOnlyList<string> paths)
    {
        IsDragOver = false;
        SelectedItem = NavigationItems.First(item => ReferenceEquals(item.Page, _library));
        await _library.AddPathsAsync(paths);
    }

    public void Dispose()
    {
        _disposed = true;
        _status.Changed -= OnStatusChanged;
        _errors.Reported -= OnErrorReported;
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

    private void OnErrorReported(object? sender, EventArgs args)
    {
        if (!_disposed)
        {
            UnhandledErrorText = UnhandledErrorReporter.UserMessage;
        }
    }

    private void UpdateStatus()
    {
        // Рядок стану короткий; пояснення («чому» і «що робити») — лише в банері.
        StatusText = _status.Stage switch
        {
            CoreStage.Starting => "Запуск…",
            CoreStage.Failed => "Помилка запуску.",
            _ when !_status.IsModelAvailable => "Готово, але без моделі.",
            _ => "Готово.",
        };

        NoticeText = _status.Stage switch
        {
            CoreStage.Failed => _status.Error,
            CoreStage.Ready when !_status.IsModelAvailable => ModelMissingNotice,
            _ => null,
        };

        HasStatusProblem = _status.Stage != CoreStage.Ready || !_status.IsModelAvailable;
    }
}
