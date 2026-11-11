using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalMind.App.Services;
using LocalMind.Core.Abstractions;
using LocalMind.Core.Errors;
using LocalMind.Core.Models;
using LocalMind.Search.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LocalMind.App.ViewModels;

/// <summary>Пошуковий запит і результати. Лише останній запущений запит може оновити сторінку.</summary>
public sealed partial class SearchViewModel : ViewModelBase
{
    private readonly ISearchService _search;
    private readonly CoreStatus _status;
    private readonly ILogger<SearchViewModel> _logger;
    private readonly ResultCardFactory _cards;
    private CancellationTokenSource? _searchCancellation;

    public SearchViewModel(ISearchService search, CoreStatus status, IOptions<SearchOptions> options,
        ILogger<SearchViewModel> logger, ResultCardFactory cards)
    {
        ArgumentNullException.ThrowIfNull(search);
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(cards);

        _search = search;
        _status = status;
        _logger = logger;
        _cards = cards;
        // Після помилки ValidateOnStart хост уже повідомив CoreStatus.Failed.
        // Повторне читання невалідних Options не повинно завадити відкриттю вікна.
        TopK = status.Stage == CoreStage.Failed ? SearchQuery.DefaultTopK : options.Value.DefaultTopK;
    }

    public IReadOnlyList<SearchMode> Modes { get; } = Array.AsReadOnly<SearchMode>([
        SearchMode.Semantic, SearchMode.Keyword, SearchMode.Hybrid,
    ]);

    [ObservableProperty]
    public partial string QueryText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial SearchMode SelectedMode { get; set; } = SearchMode.Hybrid;

    // NumericUpDown.Value — decimal?; порожнє поле теж має пройти валідацію у VM.
    [ObservableProperty]
    public partial decimal? TopK { get; set; }

    [ObservableProperty]
    public partial bool IsSearching { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<ResultCardViewModel> Results { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; private set; }

    public bool HasError => ErrorMessage is not null;

    /// <summary>Скасовує пошук при закритті застосунку; Transient-VM не утримується DI як IDisposable.</summary>
    public void Close() => _searchCancellation?.Cancel();

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SearchAsync(CancellationToken cancellationToken)
    {
        var previous = _searchCancellation;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _searchCancellation = request;
        previous?.Cancel();

        IsSearching = false;
        ErrorMessage = null;
        Results = [];

        try
        {
            if (string.IsNullOrWhiteSpace(QueryText))
            {
                ErrorMessage = "Введіть пошуковий запит.";
                return;
            }

            if (TopK is not { } topK || topK < 1 || topK > int.MaxValue || decimal.Truncate(topK) != topK)
            {
                ErrorMessage = "Кількість результатів має бути цілим числом, більшим за 0.";
                return;
            }

            if (!Enum.IsDefined(SelectedMode))
            {
                ErrorMessage = "Оберіть режим пошуку.";
                return;
            }

            if (_status.Stage == CoreStage.Starting)
            {
                ErrorMessage = "Ядро ще запускається. Спробуйте пошук після завершення запуску.";
                return;
            }

            if (_status.Stage == CoreStage.Failed)
            {
                ErrorMessage = _status.Error ?? "Помилка запуску ядра.";
                return;
            }

            if (SelectedMode != SearchMode.Keyword && !_status.IsModelAvailable)
            {
                ErrorMessage = "Для цього режиму потрібна модель. Оберіть Keyword або завантажте модель.";
                return;
            }

            var query = new SearchQuery(QueryText, SelectedMode, (int)topK);
            IsSearching = true;

            // Токенізація й частина пошуку можуть виконуватися синхронно всередині сервісу.
            // На UI-потік повертаємося після await, лише щоб опублікувати результат.
            var results = await Task.Run(async () =>
            {
                var found = await _search.SearchAsync(query, request.Token);
                return await _cards.CreateAsync(found, request.Token);
            }, request.Token);
            if (ReferenceEquals(_searchCancellation, request) && !request.IsCancellationRequested)
            {
                Results = results;
            }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
            // Новий запит або закриття сторінки: попередній результат більше не потрібен.
        }
        catch (Exception exception)
        {
            LogSearchFailed(_logger, exception);
            if (ReferenceEquals(_searchCancellation, request) && !request.IsCancellationRequested)
            {
                ErrorMessage = exception is LocalMindException
                    ? exception.Message
                    : "Не вдалося виконати пошук. Спробуйте ще раз.";
            }
        }
        finally
        {
            if (ReferenceEquals(_searchCancellation, request))
            {
                _searchCancellation = null;
                IsSearching = false;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Не вдалося виконати пошук")]
    private static partial void LogSearchFailed(ILogger logger, Exception exception);
}
