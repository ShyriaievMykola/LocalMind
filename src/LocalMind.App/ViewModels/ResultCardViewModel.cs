using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalMind.App.Services;
using LocalMind.Core.Models;
using Microsoft.Extensions.Logging;

namespace LocalMind.App.ViewModels;

/// <summary>Джерело, уривок і команда відкриття одного результату пошуку.</summary>
public sealed partial class ResultCardViewModel : ViewModelBase
{
    private readonly IFileLauncher _files;
    private readonly ILogger<ResultCardViewModel> _logger;

    public ResultCardViewModel(SearchResult result, IReadOnlyList<string> sourceFolders,
        IFileLauncher files, ILogger<ResultCardViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(sourceFolders);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(logger);

        Result = result;
        _files = files;
        _logger = logger;
        FileName = Path.GetFileName(result.DocumentPath);
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
            ErrorMessage = exception is FileNotFoundException
                ? $"Файл не знайдено: {FileName}"
                : "Не вдалося відкрити файл системною програмою.";
        }
    }

    private static string GetRelativePath(string documentPath, IReadOnlyList<string> sourceFolders)
    {
        var fullPath = Path.GetFullPath(documentPath);
        // Для вкладених відстежуваних папок показуємо шлях від найближчої папки.
        foreach (var folder in sourceFolders.Select(Path.GetFullPath).OrderByDescending(path => path.Length))
        {
            var relative = Path.GetRelativePath(folder, fullPath);
            if (!Path.IsPathRooted(relative)
                && relative != ".."
                && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                return relative;
            }
        }

        // Наприклад, папку вже прибрали зі списку, а результат ще лишився на екрані.
        return documentPath;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Не вдалося відкрити джерело: {Path}")]
    private static partial void LogOpenFailed(ILogger logger, string path, Exception exception);
}
