using LocalMind.App.ViewModels;
using LocalMind.Core.Abstractions;
using LocalMind.Core.Models;
using Microsoft.Extensions.Logging;

namespace LocalMind.App.Services;

/// <summary>Створює VM карток із сервісами DI; відстежувані папки читаються один раз на набір результатів.</summary>
public sealed class ResultCardFactory(
    IDocumentRepository repository,
    IFileLauncher files,
    ILogger<ResultCardViewModel> logger)
{
    public async Task<IReadOnlyList<ResultCardViewModel>> CreateAsync(
        IReadOnlyList<SearchResult> results,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(results);
        cancellationToken.ThrowIfCancellationRequested();
        if (results.Count == 0)
        {
            return [];
        }

        var folders = await repository.GetSourceFoldersAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return results.Select(result => new ResultCardViewModel(result, folders, files, logger)).ToArray();
    }
}
