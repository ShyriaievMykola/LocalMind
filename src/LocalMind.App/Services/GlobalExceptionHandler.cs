using Avalonia.Threading;

namespace LocalMind.App.Services;

/// <summary>
/// Підписується на необроблені винятки процесу й передає їх у <see cref="UnhandledErrorReporter"/>.
/// Винятки UI-потоку та завдань, яких ніхто не дочекався, не закривають застосунок. Виняток у потоці,
/// який створив сам код (не Task), .NET все одно завершує процес — для нього лишається лише запис у лог.
/// </summary>
internal static class GlobalExceptionHandler
{
    private static int _installed;

    /// <summary>Підписки ставляться один раз і живуть до кінця процесу; повторний виклик нічого не робить.</summary>
    public static void Install(UnhandledErrorReporter reporter)
    {
        ArgumentNullException.ThrowIfNull(reporter);

        if (Interlocked.Exchange(ref _installed, 1) == 1)
        {
            return;
        }

        Dispatcher.UIThread.UnhandledException += (_, args) =>
        {
            reporter.Report(args.Exception, "UI-потік");
            args.Handled = ShouldHandle(args.Exception);
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            // Скасований пошук чи індексація — не помилка: користувачу банер не потрібен.
            if (!IsOnlyCancellation(args.Exception))
            {
                reporter.Report(args.Exception, "фонове завдання");
            }

            args.SetObserved();
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            // Процес уже завершується, UI більше не буде: банер не покажемо, лише лог.
            if (args.ExceptionObject is Exception exception)
            {
                reporter.Log(exception, "домен застосунку");
            }
        };
    }

    /// <summary>Після нестачі пам'яті продовжувати роботу небезпечно, тому такий виняток не гасимо.</summary>
    internal static bool ShouldHandle(Exception exception) => exception is not OutOfMemoryException;

    /// <summary>Усі внутрішні винятки — скасування (зокрема <see cref="TaskCanceledException"/>).</summary>
    internal static bool IsOnlyCancellation(AggregateException exception) =>
        exception.Flatten().InnerExceptions.All(inner => inner is OperationCanceledException);
}
