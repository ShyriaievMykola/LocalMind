using System.Collections.Concurrent;
using LocalMind.App.Services;
using LocalMind.App.ViewModels;
using LocalMind.Core.Abstractions;
using LocalMind.Core.Models;
using LocalMind.Search.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LocalMind.App.Tests;

internal sealed class FakeSearchService : ISearchService
{
    public ConcurrentQueue<(SearchQuery Query, CancellationToken Token)> Calls { get; } = new();

    public Func<SearchQuery, CancellationToken, Task<IReadOnlyList<SearchResult>>> Handler { get; set; } =
        (_, _) => Task.FromResult<IReadOnlyList<SearchResult>>([]);

    public Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken cancellationToken = default)
    {
        Calls.Enqueue((query, cancellationToken));
        return Handler(query, cancellationToken);
    }

    public SearchViewModel CreateViewModel(CoreStatus? status = null, int defaultTopK = SearchQuery.DefaultTopK)
    {
        if (status is null)
        {
            status = new CoreStatus(NullLogger<CoreStatus>.Instance);
            status.SetModelAvailable();
            status.SetReady();
        }

        return new SearchViewModel(this, status, Options.Create(new SearchOptions { DefaultTopK = defaultTopK }),
            NullLogger<SearchViewModel>.Instance);
    }
}
