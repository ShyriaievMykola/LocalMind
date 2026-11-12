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
    public async Task Report_FromBackgroundThread_StillRaisesEventOnlyWhenUiRunsPending()
    {
        var reporter = new UnhandledErrorReporter(_dispatcher, _logger);
        var reported = 0;
        reporter.Reported += (_, _) => reported++;

        await Task.Run(() => reporter.Report(new InvalidOperationException("збій"), "фонове завдання"));

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

    [Fact]
    public void Report_LoggerThrows_DoesNotThrowAndStillNotifies()
    {
        _logger.ThrowOnLog = true;
        var reporter = new UnhandledErrorReporter(_dispatcher, _logger);
        var reported = 0;
        reporter.Reported += (_, _) => reported++;

        var failure = Record.Exception(() => reporter.Report(new InvalidOperationException("збій"), "UI-потік"));
        _dispatcher.RunPending();

        Assert.Null(failure);
        Assert.Equal(1, reported);
    }

    [Fact]
    public void Report_SubscriberThrows_DoesNotBreakUiAndIsLogged()
    {
        var reporter = new UnhandledErrorReporter(_dispatcher, _logger);
        reporter.Reported += (_, _) => throw new InvalidOperationException("обробник");

        reporter.Report(new InvalidOperationException("збій"), "UI-потік");
        var failure = Record.Exception(_dispatcher.RunPending);

        Assert.Null(failure);
        Assert.Equal(2, _logger.Entries.Count);
    }

    [Fact]
    public void Log_OnlyLogs_DoesNotNotifyUi()
    {
        var reporter = new UnhandledErrorReporter(_dispatcher, _logger);

        reporter.Log(new InvalidOperationException("збій"), "домен застосунку");

        Assert.Single(_logger.Entries);
        Assert.Equal(0, _dispatcher.PendingCount);
    }

    private sealed class CapturingLogger : ILogger<UnhandledErrorReporter>
    {
        public List<(LogLevel Level, Exception? Exception)> Entries { get; } = [];

        public bool ThrowOnLog { get; set; }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (ThrowOnLog)
            {
                throw new InvalidOperationException("логер зламався");
            }

            Entries.Add((logLevel, exception));
        }
    }
}
