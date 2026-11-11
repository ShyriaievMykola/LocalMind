using Avalonia.Threading;

namespace LocalMind.App.Services;

/// <summary>
/// Підписується на необроблені винятки процесу й передає їх у <see cref="UnhandledErrorReporter"/>.
/// Винятки UI-потоку та завдань, яких ніхто не дочекався, не закривають застосунок. Виняток у потоці,
/// який створив сам код (не Task), .NET все одно завершує процес — для нього лишається лише запис у лог.
/// </summary>
internal static class GlobalExceptionHandler
{
    /// <summary>Викликається один раз при старті; підписки живуть до кінця процесу.</summary>
    public static void Install(UnhandledErrorReporter reporter)
    {
        ArgumentNullException.ThrowIfNull(reporter);

        Dispatcher.UIThread.UnhandledException += (_, args) =>
        {
            reporter.Report(args.Exception, "UI-потік");
            args.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            reporter.Report(args.Exception, "фонове завдання");
            args.SetObserved();
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                reporter.Report(exception, "домен застосунку");
            }
        };
    }
}
