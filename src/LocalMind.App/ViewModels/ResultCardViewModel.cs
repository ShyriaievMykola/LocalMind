using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalMind.App.Services;
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
}
