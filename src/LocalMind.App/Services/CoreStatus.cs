using Microsoft.Extensions.Logging;

namespace LocalMind.App.Services;

/// <summary>Етап запуску ядра.</summary>
public enum CoreStage
{
    /// <summary>Міграції і завантаження векторів ще тривають.</summary>
    Starting,

    /// <summary>Ядро готове; пошук і індексація доступні (семантичний пошук — лише з моделлю).</summary>
    Ready,

    /// <summary>Ядро не запустилося (база недоступна, некоректні налаштування); причина — у <see cref="CoreStatus.Error"/>.</summary>
    Failed,
}

/// <summary>
/// Стан запуску ядра для ViewModel-ів. Змінюється з фонового потоку: обробник <see cref="Changed"/>
/// має сам перейти в UI-потік (<c>Dispatcher.UIThread.Post</c>).
/// </summary>
public sealed partial class CoreStatus(ILogger<CoreStatus> logger)
{
    private readonly Lock _lock = new();
    private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Поточний етап.</summary>
    public CoreStage Stage { get; private set; } = CoreStage.Starting;

    /// <summary>Файли моделі на місці. Без моделі не працюють семантичний пошук та індексація.</summary>
    public bool IsModelAvailable { get; private set; }

    /// <summary>Чому немає моделі — шлях, де її чекали; <see langword="null"/>, якщо модель є або ще не перевірена.</summary>
    public string? ModelError { get; private set; }

    /// <summary>Причина <see cref="CoreStage.Failed"/>; інакше <see langword="null"/>.</summary>
    public string? Error { get; private set; }

    /// <summary>Завершується, коли запуск закінчився — успішно чи ні (без винятку).</summary>
    public Task Completion => _completed.Task;

    /// <summary>
    /// Стан змінився. Викликається з фонового потоку. Виняток в обробнику логується і не зупиняє
    /// ні запуск, ні інших обробників.
    /// </summary>
    public event EventHandler? Changed;

    internal void SetModelAvailable()
    {
        lock (_lock)
        {
            IsModelAvailable = true;
            ModelError = null;
        }

        RaiseChanged();
    }

    internal void SetModelMissing(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);

        lock (_lock)
        {
            IsModelAvailable = false;
            ModelError = error;
        }

        RaiseChanged();
    }

    internal void SetReady() => Finish(CoreStage.Ready, error: null);

    internal void SetFailed(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);

        Finish(CoreStage.Failed, error);
    }

    private void Finish(CoreStage stage, string? error)
    {
        lock (_lock)
        {
            if (Stage != CoreStage.Starting)
            {
                return;
            }

            Stage = stage;
            Error = error;
        }

        // Спершу завершуємо Completion: збій обробника події не має лишити очікувачів назавжди.
        _completed.TrySetResult();
        RaiseChanged();
    }

    private void RaiseChanged()
    {
        if (Changed is not { } handlers)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<EventHandler>())
        {
            try
            {
                handler(this, EventArgs.Empty);
            }
            catch (Exception exception)
            {
                LogHandlerFailed(logger, exception);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Обробник CoreStatus.Changed кинув виняток")]
    private static partial void LogHandlerFailed(ILogger logger, Exception exception);
}
