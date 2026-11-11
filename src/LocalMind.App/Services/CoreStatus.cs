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
public sealed class CoreStatus
{
    private readonly Lock _lock = new();
    private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Поточний етап.</summary>
    public CoreStage Stage { get; private set; } = CoreStage.Starting;

    /// <summary>Файли моделі на місці. Без моделі не працюють семантичний пошук та індексація.</summary>
    public bool IsModelAvailable { get; private set; }

    /// <summary>Чому немає моделі — шлях, де її чекали; <see langword="null"/>, якщо модель є.</summary>
    public string? ModelError { get; private set; }

    /// <summary>Причина <see cref="CoreStage.Failed"/>; інакше <see langword="null"/>.</summary>
    public string? Error { get; private set; }

    /// <summary>Завершується, коли запуск закінчився — успішно чи ні (без винятку).</summary>
    public Task Completion => _completed.Task;

    /// <summary>Стан змінився. Викликається з фонового потоку.</summary>
    public event EventHandler? Changed;

    internal void SetModel(bool isAvailable, string? error)
    {
        lock (_lock)
        {
            IsModelAvailable = isAvailable;
            ModelError = error;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    internal void SetReady() => Finish(CoreStage.Ready, error: null);

    internal void SetFailed(string error) => Finish(CoreStage.Failed, error);

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

        Changed?.Invoke(this, EventArgs.Empty);
        _completed.TrySetResult();
    }
}
