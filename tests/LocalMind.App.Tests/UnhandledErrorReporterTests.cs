using LocalMind.App.Services;
using Microsoft.Extensions.Logging;

namespace LocalMind.App.Tests;

public sealed class UnhandledErrorReporterTests
{
    private readonly FakeUiDispatcher _dispatcher = new();
    private readonly CapturingLogger _logger = new();

    [Fact]
    public void Report_LogsExceptionAndNotifiesOnlyThroughUiDispatcher()
    {
        var reporter = new UnhandledErrorReporter(_dispatcher, _logger);
        var reported = 0;
        reporter.Reported += (_, _) => reported++;
        var exception = new InvalidOperationException("збій");

        reporter.Report(exception, "UI-потік");

        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Same(exception, entry.Exception);
        Assert.Equal(0, reported);

        _dispatcher.RunPending();

        Assert.Equal(1, reported);
    }

    [Fact]
    public void Report_FromBackgroundThread_StillRaisesEventOnlyWhenUiRunsPending()
    {
        var reporter = new UnhandledErrorReporter(_dispatcher, _logger);
        var reported = 0;
        reporter.Reported += (_, _) => reported++;

        Task.Run(() => reporter.Report(new InvalidOperationException("збій"), "фонове завдання")).GetAwaiter().GetResult();

        Assert.Equal(0, reported);
        Assert.Equal(1, _dispatcher.PendingCount);

        _dispatcher.RunPending();

        Assert.Equal(1, reported);
    }

    [Fact]
    public void Report_InvalidArguments_Throw()
    {
        var reporter = new UnhandledErrorReporter(_dispatcher, _logger);

        Assert.Throws<ArgumentNullException>(() => reporter.Report(null!, "UI-потік"));
        Assert.Throws<ArgumentException>(() => reporter.Report(new InvalidOperationException(), " "));
    }

    private sealed class CapturingLogger : ILogger<UnhandledErrorReporter>
    {
        public List<(LogLevel Level, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, exception));
    }
}
