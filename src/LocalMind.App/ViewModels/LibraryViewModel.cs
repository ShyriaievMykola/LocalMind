using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalMind.App.Services;
using LocalMind.Core.Abstractions;
using LocalMind.Core.Models;
using Microsoft.Extensions.Logging;

namespace LocalMind.App.ViewModels;

/// <summary>
/// Сторінка бібліотеки: відстежувані папки і документи зі станом індексації.
/// Події ядра приходять з фонових потоків і переносяться в UI-потік через <see cref="IUiDispatcher"/>.
/// </summary>
public sealed partial class LibraryViewModel : ViewModelBase
{
    private const string ModelMissingMessage = "Для індексації потрібна модель. Запустіть scripts/download-models.";
    private const string LoadFailedMessage = "Не вдалося завантажити бібліотеку.";
    
    // Windows і macOS за замовчуванням не розрізняють регістр у шляхах, Linux розрізняє.
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private readonly IIndexingService _indexing;
    private readonly IDocumentRepository _repository;
    private readonly HashSet<string> _supportedExtensions;
    private readonly string _supportedList;
    private readonly IFolderPicker _folderPicker;
    private readonly IUiDispatcher _dispatcher;
    private readonly CoreStatus _status;
    private readonly ILogger<LibraryViewModel> _logger;
    private readonly EventHandler<IndexingProgress> _progressHandler;
    private readonly EventHandler _statusHandler;
    private int _refreshVersion;
    private volatile bool _closed;

    public LibraryViewModel(
        IIndexingService indexing,
        IDocumentRepository repository,
        IEnumerable<IDocumentParser> parsers,
        IFolderPicker folderPicker,
        IUiDispatcher dispatcher,
        CoreStatus status,
        ILogger<LibraryViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(indexing);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(parsers);
        ArgumentNullException.ThrowIfNull(folderPicker);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(logger);

        _indexing = indexing;
        _repository = repository;
        _supportedExtensions = new HashSet<string>(parsers.SelectMany(p => p.SupportedExtensions), StringComparer.OrdinalIgnoreCase);
        _supportedList = string.Join(", ", _supportedExtensions.Order(StringComparer.Ordinal));
        _folderPicker = folderPicker;
        _dispatcher = dispatcher;
        _status = status;
        _logger = logger;

        _progressHandler = (_, _) => PostToUi(OnProgressChanged);
        _statusHandler = (_, _) => PostToUi(OnCoreStatusChanged);
        _indexing.ProgressChanged += _progressHandler;
        _status.Changed += _statusHandler;

        ShowProgress();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFolders))]
    public partial IReadOnlyList<string> Folders { get; private set; } = [];

    public bool HasFolders => Folders.Count > 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RequestRemoveFolderCommand))]
    public partial string? SelectedFolder { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirmingRemoval))]
    public partial string? FolderPendingRemoval { get; private set; }

    public bool IsConfirmingRemoval => FolderPendingRemoval is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDocuments), nameof(DocumentCount), nameof(ErrorCount), nameof(SummaryText))]
    public partial IReadOnlyList<LibraryDocumentItem> Documents { get; private set; } = [];

    public bool HasDocuments => Documents.Count > 0;

    public int DocumentCount => Documents.Count;

    public int ErrorCount => Documents.Count(d => d.IsError);

    public string SummaryText => $"Документів: {DocumentCount}, з помилками: {ErrorCount}";

    /// <summary>
    /// Прогрес поточної індексації; <see langword="null"/>, коли черга порожня. Сторінці він потрібен,
    /// щоб оновити список після завершення; сам прогрес показує рядок стану (<see cref="IndexingProgressViewModel"/>).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIndexing))]
    public partial IndexingProgress? Progress { get; private set; }

    public bool IsIndexing => Progress is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; private set; }

    public bool HasError => ErrorMessage is not null;
       
    /// <summary>Пояснення без помилки (наприклад, що для файла додано його папку); <see langword="null"/> — нічого пояснювати.</summary>
    [ObservableProperty]
    public partial string? NoticeMessage { get; private set; }

    /// <summary>Відписується від подій ядра при закритті вікна: сторінка Transient, DI її не звільняє.</summary>
    public void Close()
    {
        _closed = true;
        _indexing.ProgressChanged -= _progressHandler;
        _status.Changed -= _statusHandler;
    }

    /// <summary>
    /// Перечитує папки й документи. Спрацьовує, коли відкривають сторінку, коли ядро стало готовим
    /// і коли черга індексації спорожніла. Опублікувати результат може лише останнє оновлення.
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task RefreshAsync()
    {
        // Поки ядро не готове, база ще може бути не створена.
        if (_status.Stage != CoreStage.Ready)
        {
            return;
        }

        var version = Interlocked.Increment(ref _refreshVersion);
        try
        {
            var folders = await _indexing.GetFoldersAsync();
            var documents = await _repository.GetDocumentsAsync();
            var chunkCounts = await _repository.GetChunkCountsAsync();
            if (version != Volatile.Read(ref _refreshVersion) || _closed)
            {
                return;
            }

            // ListBox скидає вибір, коли отримує новий список, і через TwoWay пише null у SelectedFolder.
            // Тому запам'ятовуємо вибрану папку і повертаємо її, якщо вона лишилась у бібліотеці.
            var selected = SelectedFolder;
            Folders = [.. folders.Order(StringComparer.OrdinalIgnoreCase)];
            SelectedFolder = selected is not null && Folders.Contains(selected) ? selected : null;

            Documents = [.. documents
                .Select(d => new LibraryDocumentItem(d.Path, d.Status, chunkCounts.GetValueOrDefault(d.Id), d.Error))
                .OrderBy(d => d.Path, StringComparer.OrdinalIgnoreCase)];

            // Прибираємо лише помилку завантаження. Помилку дії користувача («Папку не знайдено»)
            // фонове оновлення не повинно ховати.
            if (ErrorMessage == LoadFailedMessage)
            {
                ErrorMessage = null;
            }
        }
        catch (Exception exception)
        {
            LogRefreshFailed(_logger, exception);
            if (version == Volatile.Read(ref _refreshVersion) && !_closed)
            {
                ErrorMessage = LoadFailedMessage;
            }
        }
    }

    [RelayCommand]
    private async Task AddFolderAsync()
    {
        ErrorMessage = GetIndexingBlocker();
        if (ErrorMessage is not null)
        {
            return;
        }

        string? folder;
        try
        {
            folder = await _folderPicker.PickFolderAsync();
        }
        catch (Exception exception)
        {
            LogActionFailed(_logger, "вибір папки", exception);
            ErrorMessage = "Не вдалося відкрити вибір папки.";
            return;
        }

        if (folder is null)
        {
            return;
        }

        try
        {
            // Перелік файлів великої папки займає час, тому не на UI-потоці.
            await Task.Run(() => _indexing.AddFolderAsync(folder));
        }
        catch (DirectoryNotFoundException)
        {
            ErrorMessage = $"Папку не знайдено: {folder}";
            return;
        }
        catch (Exception exception)
        {
            LogActionFailed(_logger, "додавання папки", exception);
            ErrorMessage = "Не вдалося додати папку.";
            return;
        }

        await RefreshAsync();
    }
    /// <summary>
    /// Додає перетягнуті у вікно файли й папки (A-11). Папка стає відстежуваною. Для файла додаємо його папку:
    /// бібліотека стежить лише за папками, тож так вона побачить і подальші зміни файла, і сусідні нотатки.
    /// </summary>
    public async Task AddPathsAsync(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        NoticeMessage = null;
        ErrorMessage = GetIndexingBlocker();
        if (ErrorMessage is not null)
        {
            return;
        }

        var folders = new List<string>();
        var viaFolder = new List<string>();
        var alreadyTracked = new List<string>();
        var unsupported = new List<string>();

        foreach (var path in paths.Where(p => !string.IsNullOrWhiteSpace(p)).Select(NormalizePath))
        {
            string folder;
            string? fileName = null;
            if (Directory.Exists(path))
            {
                folder = path;
            }
            else if (File.Exists(path)
                     && _supportedExtensions.Contains(Path.GetExtension(path))
                     && Path.GetDirectoryName(path) is { } parent)
            {
                folder = parent;
                fileName = Path.GetFileName(path);
            }
            else
            {
                // Непідтримуваний файл або шлях, якого вже немає: користувачу треба знати, що його не додано.
                unsupported.Add(Path.GetFileName(path));
                continue;
            }

            if (Folders.Any(tracked => IsInside(folder, tracked)))
            {
                alreadyTracked.Add(fileName ?? Path.GetFileName(folder));
            }
            else
            {
                if (!folders.Any(added => added.Equals(folder, PathComparison)))
                {
                    folders.Add(folder);
                }

                if (fileName is not null)
                {
                    viaFolder.Add(fileName);
                }
            }
        }

        var failed = new List<string>();
        foreach (var folder in folders)
        {
            try
            {
                // Перелік файлів великої папки займає час, тому не на UI-потоці.
                await Task.Run(() => _indexing.AddFolderAsync(folder));
            }
            catch (Exception exception)
            {
                LogActionFailed(_logger, "додавання перетягнутої папки", exception);
                failed.Add(folder);
            }
        }

        ErrorMessage = DescribeDropProblems(unsupported, failed, nothingRecognized: folders.Count + alreadyTracked.Count == 0);
        NoticeMessage = DescribeDropNotice(viaFolder, alreadyTracked);

        if (folders.Count > failed.Count)
        {
            await RefreshAsync();
        }
    }

    private string? DescribeDropProblems(List<string> unsupported, List<string> failed, bool nothingRecognized)
    {
        var parts = new List<string>();
        if (unsupported.Count > 0)
        {
            parts.Add($"Не додано: {string.Join(", ", unsupported)} — можна перетягнути папку або файли {_supportedList}.");
        }

        if (failed.Count > 0)
        {
            parts.Add($"Не вдалося додати папку: {string.Join(", ", failed)}.");
        }

        if (parts.Count == 0 && nothingRecognized)
        {
            parts.Add("Перетягніть папку або файл з нотатками.");
        }

        return parts.Count > 0 ? string.Join(" ", parts) : null;
    }

    private static string? DescribeDropNotice(List<string> viaFolder, List<string> alreadyTracked)
    {
        var parts = new List<string>();
        if (viaFolder.Count > 0)
        {
            parts.Add($"Для файлів {string.Join(", ", viaFolder)} додано їхню папку — бібліотека стежитиме і за сусідніми нотатками.");
        }

        if (alreadyTracked.Count > 0)
        {
            parts.Add($"Уже є в бібліотеці: {string.Join(", ", alreadyTracked)}.");
        }

        return parts.Count > 0 ? string.Join(" ", parts) : null;
    }

    private static string NormalizePath(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool IsInside(string path, string folder) =>
        path.Equals(folder, PathComparison)
        || (path.StartsWith(folder, PathComparison)
            && (folder[^1] is '/' or '\\' || path[folder.Length] is '/' or '\\'));

    [RelayCommand(CanExecute = nameof(CanRequestRemoveFolder))]
    private void RequestRemoveFolder() => FolderPendingRemoval = SelectedFolder;

    private bool CanRequestRemoveFolder() => SelectedFolder is not null;

    [RelayCommand]
    private void CancelRemoveFolder() => FolderPendingRemoval = null;

    [RelayCommand]
    private async Task ConfirmRemoveFolderAsync()
    {
        if (FolderPendingRemoval is not { } folder)
        {
            return;
        }

        FolderPendingRemoval = null;
        ErrorMessage = GetCoreBlocker();
        if (ErrorMessage is not null)
        {
            return;
        }

        try
        {
            await Task.Run(() => _indexing.RemoveFolderAsync(folder));
        }
        catch (Exception exception)
        {
            LogActionFailed(_logger, "видалення папки", exception);
            ErrorMessage = "Не вдалося прибрати папку.";
            return;
        }

        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RescanAsync()
    {
        ErrorMessage = GetIndexingBlocker();
        if (ErrorMessage is not null)
        {
            return;
        }

        try
        {
            await Task.Run(() => _indexing.RescanAsync());
        }
        catch (Exception exception)
        {
            LogActionFailed(_logger, "пересканування", exception);
            ErrorMessage = "Не вдалося пересканувати папки.";
            return;
        }

        await RefreshAsync();
    }

    private string? GetCoreBlocker() => _status.Stage switch
    {
        CoreStage.Starting => "Ядро ще запускається. Спробуйте за мить.",
        CoreStage.Failed => _status.Error ?? "Помилка запуску ядра.",
        _ => null,
    };

    /// <summary>Без моделі кожен файл отримав би статус «помилка», тому індексацію не запускаємо.</summary>
    private string? GetIndexingBlocker() =>
        GetCoreBlocker() ?? (_status.IsModelAvailable ? null : ModelMissingMessage);

    /// <summary>Читає поточний прогрес із сервісу, а не з аргументу події: події з різних потоків можуть прийти не по черзі.</summary>
    private void ShowProgress()
    {
        var progress = _indexing.Progress;
        Progress = progress.IsCompleted ? null : progress;
    }

    private void OnProgressChanged()
    {
        ShowProgress();
        if (Progress is null)
        {
            _ = RefreshCommand.ExecuteAsync(null);
        }
    }

    private void OnCoreStatusChanged()
    {
        if (_status.Stage == CoreStage.Ready)
        {
            _ = RefreshCommand.ExecuteAsync(null);
        }
    }

    /// <summary>Події ядра приходять з фонових потоків; змінювати властивості, прив'язані до UI, можна лише в UI-потоці.</summary>
    private void PostToUi(Action action) => _dispatcher.Post(() =>
    {
        if (!_closed)
        {
            action();
        }
    });

    [LoggerMessage(Level = LogLevel.Error, Message = "Не вдалося завантажити бібліотеку")]
    private static partial void LogRefreshFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Дія з бібліотекою не вдалася: {Action}")]
    private static partial void LogActionFailed(ILogger logger, string action, Exception exception);
}