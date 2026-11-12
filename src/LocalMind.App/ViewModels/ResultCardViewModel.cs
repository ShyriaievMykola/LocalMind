using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalMind.App.Services;
using LocalMind.Core.Abstractions;
using LocalMind.Core.Errors;
using LocalMind.Core.Models;
using Microsoft.Extensions.Logging;

namespace LocalMind.App.ViewModels;

/// <summary>Джерело, уривок і команда відкриття одного результату пошуку.</summary>
public sealed partial class ResultCardViewModel : ViewModelBase
{
    // Те саме правило, що в Core: Windows/macOS без регістру, Linux — з регістром.
    // TODO(#86): використовувати FilePaths.Comparison після merge #109.
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private readonly IFileLauncher _files;
    private readonly ILogger<ResultCardViewModel> _logger;
    private readonly IDocumentRepository _repository;
    private CancellationTokenSource? _contextCancellation;

    public ResultCardViewModel(SearchResult result, IReadOnlyList<string> sourceFolders,
        IFileLauncher files, ILogger<ResultCardViewModel> logger, IDocumentRepository repository)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(sourceFolders);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(repository);

        Result = result;
        _files = files;
        _logger = logger;
        _repository = repository;
        FileName = GetFileName(result.DocumentPath);
        RelativePath = GetRelativePath(result.DocumentPath, sourceFolders);
    }

    public SearchResult Result { get; }

    public string FileName { get; }

    public string DocumentPath => Result.DocumentPath;

    public string RelativePath { get; }

    public double Score => Result.Score;

    public Snippet Snippet => Result.Snippet;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; private set; }

    public bool HasError => ErrorMessage is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ContextToggleText))]
    [NotifyPropertyChangedFor(nameof(CanRetryContext))]
    [NotifyCanExecuteChangedFor(nameof(RetryContextCommand))]
    public partial bool IsContextExpanded { get; private set; }

    public string ContextToggleText => IsContextExpanded ? "Менше контексту" : "Більше контексту";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRetryContext))]
    [NotifyCanExecuteChangedFor(nameof(RetryContextCommand))]
    public partial bool IsContextLoading { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<ResultContextChunk> ContextChunks { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasContextError))]
    [NotifyPropertyChangedFor(nameof(CanRetryContext))]
    [NotifyCanExecuteChangedFor(nameof(RetryContextCommand))]
    public partial string? ContextError { get; private set; }

    public bool HasContextError => ContextError is not null;

    public bool CanRetryContext => IsContextExpanded && HasContextError && !IsContextLoading;

    /// <summary>Скасовує завантаження при згортанні, заміні результатів або закритті застосунку.</summary>
    public void CloseContext()
    {
        var previous = _contextCancellation;
        _contextCancellation = null;
        previous?.Cancel();
        IsContextExpanded = false;
        IsContextLoading = false;
        ContextChunks = [];
        ContextError = null;
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ToggleContextAsync(CancellationToken cancellationToken)
    {
        if (IsContextExpanded)
        {
            CloseContext();
            return;
        }

        IsContextExpanded = true;
        await LoadContextAsync(cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanRetryContext))]
    private Task RetryContextAsync(CancellationToken cancellationToken) => LoadContextAsync(cancellationToken);

    private async Task LoadContextAsync(CancellationToken cancellationToken)
    {
        using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var previous = _contextCancellation;
        _contextCancellation = request;
        previous?.Cancel();
        ContextError = null;
        ContextChunks = [];
        IsContextLoading = true;

        try
        {
            var match = Result.Chunk;
            var from = Math.Max(0, match.Ordinal - 1);
            var to = match.Ordinal == int.MaxValue ? int.MaxValue : match.Ordinal + 1;
            // Навіть синхронна частина SQLite-запиту не повинна блокувати UI.
            var chunks = await Task.Run(() => _repository.GetChunksByDocumentAsync(
                match.DocumentId, from, to, request.Token), request.Token);
            if (!IsCurrentContext(request))
            {
                return;
            }

            // Переіндексація замінює фрагменти. Старий результат не змішуємо з новою версією документа.
            if (!chunks.Contains(match))
            {
                ContextError = "Документ змінено або видалено. Виконайте пошук знову.";
                return;
            }

            ContextChunks = [.. chunks.Select(chunk => new ResultContextChunk(chunk, match.Ordinal, CreateContextSnippet(chunk)))];
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
            if (ReferenceEquals(_contextCancellation, request))
            {
                CloseContext();
            }
        }
        catch (Exception exception)
        {
            if (IsCurrentContext(request))
            {
                LogContextFailed(_logger, Result.Chunk.DocumentId, exception);
                ContextError = "Не вдалося завантажити контекст. Спробуйте ще раз.";
            }
        }
        finally
        {
            if (ReferenceEquals(_contextCancellation, request))
            {
                if (request.IsCancellationRequested)
                {
                    CloseContext();
                }
                else
                {
                    _contextCancellation = null;
                    IsContextLoading = false;
                }
            }
        }
    }

    private bool IsCurrentContext(CancellationTokenSource request) =>
        ReferenceEquals(_contextCancellation, request) && IsContextExpanded && !request.IsCancellationRequested;

    private Snippet CreateContextSnippet(Chunk chunk) => new(chunk.Text, 0,
        chunk.Id == Result.Chunk.Id
            // У картці діапазони відносні до короткого уривка, тут — до повного фрагмента.
            ? [.. Snippet.Highlights.Select(range => new HighlightRange(Snippet.Offset + range.Start, range.Length))]
            : []);

    [RelayCommand]
    private async Task OpenSourceAsync(CancellationToken cancellationToken)
    {
        ErrorMessage = null;
        try
        {
            await _files.OpenAsync(DocumentPath, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Скасовано до відкриття системної програми.
        }
        catch (Exception exception)
        {
            LogOpenFailed(_logger, DocumentPath, exception);
            ErrorMessage = exception switch
            {
                FileNotFoundException => $"Файл не знайдено: {FileName}",
                LocalMindException => exception.Message,
                _ => "Не вдалося відкрити файл системною програмою.",
            };
        }
    }

    private static string GetFileName(string documentPath)
    {
        try
        {
            return Path.GetFileName(documentPath);
        }
        catch (Exception exception) when (IsInvalidPath(exception))
        {
            return documentPath;
        }
    }

    private static string GetRelativePath(string documentPath, IReadOnlyList<string> sourceFolders)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(documentPath);
        }
        catch (Exception exception) when (IsInvalidPath(exception))
        {
            return documentPath;
        }

        var nearestRootLength = 0;
        var relativePath = documentPath;
        // Для вкладених відстежуваних папок показуємо шлях від найближчої папки.
        foreach (var sourceFolder in sourceFolders)
        {
            string prefix;
            try
            {
                var folder = Path.GetFullPath(sourceFolder);
                prefix = Path.EndsInDirectorySeparator(folder) ? folder : folder + Path.DirectorySeparatorChar;
            }
            catch (Exception exception) when (IsInvalidPath(exception))
            {
                // Пошкоджений запис папки не повинен приховати решту коректних результатів.
                continue;
            }

            if (prefix.Length > nearestRootLength && fullPath.StartsWith(prefix, PathComparison))
            {
                nearestRootLength = prefix.Length;
                relativePath = fullPath[prefix.Length..];
            }
        }

        // Наприклад, папку вже прибрали зі списку, а результат ще лишився на екрані.
        return relativePath;
    }

    private static bool IsInvalidPath(Exception exception) =>
        exception is ArgumentException or NotSupportedException or PathTooLongException;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Не вдалося відкрити джерело: {Path}")]
    private static partial void LogOpenFailed(ILogger logger, string path, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Не вдалося завантажити контекст документа: {DocumentId}")]
    private static partial void LogContextFailed(ILogger logger, Guid documentId, Exception exception);
}
