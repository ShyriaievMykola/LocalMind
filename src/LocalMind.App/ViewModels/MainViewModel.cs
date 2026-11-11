using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalMind.App.Services;

namespace LocalMind.App.ViewModels;

/// <summary>Навігація головного вікна, стан запуску ядра і повідомлення про необроблені помилки.</summary>
public sealed partial class MainViewModel : ViewModelBase, IDisposable
{
    private const string ModelMissingNotice = "Модель не завантажена — запустіть scripts/download-models. Зараз працює лише пошук Keyword.";

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
        StatusText = _status.Stage switch
        {
            CoreStage.Starting => "Запуск…",
            CoreStage.Failed => $"Помилка запуску. {_status.Error}",
            _ when !_status.IsModelAvailable => $"Готово, але без моделі. {_status.ModelError}",
            _ => "Готово.",
        };

        NoticeText = _status.Stage switch
        {
            CoreStage.Failed => _status.Error,
            _ when _status.ModelError is not null => ModelMissingNotice,
            _ => null,
        };
        HasStatusProblem = _status.Stage != CoreStage.Ready || !_status.IsModelAvailable;
    }
}
