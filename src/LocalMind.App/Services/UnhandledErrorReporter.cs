using Microsoft.Extensions.Logging;

namespace LocalMind.App.Services;

/// <summary>
/// Приймає необроблені винятки: пише їх у лог і повідомляє UI, щоб показати користувачу, що сталася помилка.
/// Може викликатися з будь-якого потоку; подія <see cref="Reported"/> завжди приходить у UI-потоці.
/// </summary>
public sealed partial class UnhandledErrorReporter(IUiDispatcher dispatcher, ILogger<UnhandledErrorReporter> logger)
{
    /// <summary>Текст для користувача: без технічних деталей, які він не зможе використати.</summary>
    public const string UserMessage = "Сталася неочікувана помилка, але застосунок продовжує працювати. Подробиці записано в лог.";

    /// <summary>Повідомлено про необроблений виняток. Викликається в UI-потоці.</summary>
    public event EventHandler? Reported;

    /// <param name="exception">Необроблений виняток.</param>
    /// <param name="source">Звідки він прийшов: UI-потік, фонове завдання, домен застосунку.</param>
    public void Report(Exception exception, string source)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        LogUnhandled(logger, source, exception);
        dispatcher.Post(() => Reported?.Invoke(this, EventArgs.Empty));
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Необроблений виняток ({Source})")]
    private static partial void LogUnhandled(ILogger logger, string source, Exception exception);
}
