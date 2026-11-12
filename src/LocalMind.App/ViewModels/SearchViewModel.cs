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

/// <summary>
/// Пошуковий запит, результати, історія запитів і керування з клавіатури.
/// Лише останній запущений запит може оновити сторінку.
/// </summary>
public sealed partial class SearchViewModel : ViewModelBase
{
    /// <summary>Скільки підказок з історії показувати під полем пошуку.</summary>
    private const int MaxSuggestions = 5;

    private readonly ISearchService _search;
    private readonly CoreStatus _status;
    private readonly ILogger<SearchViewModel> _logger;
    private readonly ResultCardFactory _cards;
    private readonly ISearchHistory _history;
    private CancellationTokenSource? _searchCancellation;
    private string? _noResultsQuery;
    private bool _suppressSuggestions;
    private bool _focusPending;

    public SearchViewModel(ISearchService search, CoreStatus status, IOptions<SearchOptions> options,
        ILogger<SearchViewModel> logger, ResultCardFactory cards, ISearchHistory history)
    {
        ArgumentNullException.ThrowIfNull(search);
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(history);

        _search = search;
        _status = status;
        _logger = logger;
        _cards = cards;
        _history = history;
        // Після помилки ValidateOnStart хост уже повідомив CoreStatus.Failed.
        // Повторне читання невалідних Options не повинно завадити відкриттю вікна.
        var defaults = status.Stage == CoreStage.Failed ? new SearchOptions() : options.Value;
        TopK = defaults.DefaultTopK;
        SelectedMode = defaults.DefaultMode;
        UpdateSuggestions();
    }

    /// <summary>Сторінка просить View поставити курсор у поле пошуку (див. <see cref="RequestFocus"/>).</summary>
    public event EventHandler? FocusRequested;

    public IReadOnlyList<SearchMode> Modes { get; } = Array.AsReadOnly<SearchMode>([
        SearchMode.Semantic, SearchMode.Keyword, SearchMode.Hybrid,
    ]);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoResults))]
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

    /// <summary>Результат, вибраний стрілками; Enter відкриває його джерело.</summary>
    [ObservableProperty]
    public partial ResultCardViewModel? SelectedResult { get; set; }

    /// <summary>Запити з історії, що містять введений текст (або останні, якщо поле порожнє).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSuggestions))]
    public partial IReadOnlyList<string> Suggestions { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSuggestions))]
    public partial bool AreSuggestionsOpen { get; private set; }

    /// <summary>Чи показувати підказки під полем пошуку.</summary>
    public bool HasSuggestions => AreSuggestionsOpen && Suggestions.Count > 0;

    /// <summary>Підказка, вибрана стрілками; Enter шукає її.</summary>
    [ObservableProperty]
    public partial string? SelectedSuggestion { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; private set; }

    public bool HasError => ErrorMessage is not null;

    /// <summary>Пошук за цим текстом завершився успішно, але нічого не знайшов; зникає зі зміною тексту й з новим запитом.</summary>
    public bool HasNoResults => _noResultsQuery is not null && _noResultsQuery == QueryText;

    /// <summary>Скасовує пошук при закритті застосунку; Transient-VM не утримується DI як IDisposable.</summary>
    public void Close() => _searchCancellation?.Cancel();

    /// <summary>Ctrl+K / Ctrl+F: курсор у поле пошуку й останні запити під ним.</summary>
    public void RequestFocus()
    {
        _focusPending = true;
        AreSuggestionsOpen = true;
        UpdateSuggestions();
        FocusRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Для View: чи є невиконаний запит на фокус; позначає його виконаним. Потрібно, бо після перемикання
    /// сторінки View ще не створено, коли запит надходить, — нова View забирає його, щойно з'явиться.
    /// </summary>
    public bool TakeFocusRequest()
    {
        var pending = _focusPending;
        _focusPending = false;
        return pending;
    }

    
    /// <summary>Поле пошуку отримало фокус: показуємо останні запити, навіть якщо ще нічого не введено.</summary>
    public void ShowSuggestions()
    {
        AreSuggestionsOpen = true;
        UpdateSuggestions();
    }

    partial void OnQueryTextChanged(string value)
    {
        // Новий текст — старий вибір результату вже не відповідає запиту, Enter має шукати.
        SelectedResult = null;
        if (!_suppressSuggestions)
        {
            AreSuggestionsOpen = true;
        }

        UpdateSuggestions();
    }

    partial void OnResultsChanged(IReadOnlyList<ResultCardViewModel> value) => SelectedResult = null;

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SearchAsync(CancellationToken cancellationToken)
    {
        var previous = _searchCancellation;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _searchCancellation = request;
        previous?.Cancel();

        IsSearching = false;
        ErrorMessage = null;
        AreSuggestionsOpen = false;
        SetNoResults(null);
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

            var searchedText = QueryText;
            var query = new SearchQuery(QueryText, SelectedMode, (int)topK);
            await RememberAsync(searchedText);
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
                SetNoResults(results.Count == 0 ? searchedText : null);
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

    /// <summary>↓: наступна підказка, а якщо підказок не видно — наступний результат.</summary>
    [RelayCommand]
    private void MoveDown() => Move(+1);

    /// <summary>↑: попередня підказка або попередній результат.</summary>
    [RelayCommand]
    private void MoveUp() => Move(-1);

    /// <summary>Enter: вибрана підказка → шукаємо її; вибраний результат → відкриваємо джерело; інакше — пошук.</summary>
    [RelayCommand]
    private async Task SubmitAsync()
    {
        if (HasSuggestions && SelectedSuggestion is { } suggestion)
        {
            await ApplySuggestionAsync(suggestion);
        }
        else if (SelectedResult is { } result)
        {
            await result.OpenSourceCommand.ExecuteAsync(null);
        }
        else
        {
            await SearchCommand.ExecuteAsync(null);
        }
    }

    /// <summary>Esc: спершу закриває підказки, повторно — очищає запит і результати.</summary>
    [RelayCommand]
    private void Escape()
    {
        if (HasSuggestions)
        {
            AreSuggestionsOpen = false;
            return;
        }

        _searchCancellation?.Cancel();
        SetQueryWithoutSuggestions(string.Empty);
        Results = [];
        ErrorMessage = null;
        SetNoResults(null);
    }

    /// <summary>Підставляє запит з історії і шукає його.</summary>
    [RelayCommand]
    private Task ApplySuggestionAsync(string suggestion)
    {
        SetQueryWithoutSuggestions(suggestion);
        AreSuggestionsOpen = false;
        return SearchCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        try
        {
            await _history.ClearAsync();
        }
        catch (Exception exception)
        {
            LogHistoryFailed(_logger, exception);
            ErrorMessage = "Не вдалося очистити історію пошуку.";
        }

        UpdateSuggestions();
    }

    private void Move(int step)
    {
        if (HasSuggestions)
        {
            SelectedSuggestion = Step(Suggestions, SelectedSuggestion, step);
        }
        else if (Results.Count > 0)
        {
            SelectedResult = Step(Results, SelectedResult, step);
        }
    }

    /// <summary>Сусідній елемент списку; з «нічого не вибрано» ↓ веде на перший, ↑ — на останній.</summary>
    private static T Step<T>(IReadOnlyList<T> items, T? current, int step)
        where T : class
    {
        var index = -1;
        for (var i = 0; i < items.Count; i++)
        {
            if (EqualityComparer<T>.Default.Equals(items[i], current))
            {
                index = i;
                break;
            }
        }

        var next = index < 0
            ? (step > 0 ? 0 : items.Count - 1)
            : Math.Clamp(index + step, 0, items.Count - 1);
        return items[next];
    }

    /// <summary>Історія — допоміжна річ: якщо файл не записався, пошук усе одно виконується.</summary>
    private async Task RememberAsync(string query)
    {
        try
        {
            await _history.AddAsync(query);
        }
        catch (Exception exception)
        {
            LogHistoryFailed(_logger, exception);
        }

        UpdateSuggestions();
    }

    private void UpdateSuggestions()
    {
        var text = QueryText.Trim();
        Suggestions = [.. _history.Items
            .Where(item => !item.Equals(text, StringComparison.OrdinalIgnoreCase)
                && (text.Length == 0 || item.Contains(text, StringComparison.OrdinalIgnoreCase)))
            .Take(MaxSuggestions)];
        SelectedSuggestion = null;
    }

    /// <summary>Програмна зміна тексту (підказка, Esc) не повинна знову відкривати підказки.</summary>
    private void SetQueryWithoutSuggestions(string text)
    {
        _suppressSuggestions = true;
        try
        {
            QueryText = text;
        }
        finally
        {
            _suppressSuggestions = false;
        }
    }

    private void SetNoResults(string? query)
    {
        _noResultsQuery = query;
        OnPropertyChanged(nameof(HasNoResults));
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Не вдалося виконати пошук")]
    private static partial void LogSearchFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Не вдалося оновити історію пошуку")]
    private static partial void LogHistoryFailed(ILogger logger, Exception exception);
}