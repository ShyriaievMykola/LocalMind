using LocalMind.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace LocalMind.Core.Tests.Diagnostics;

public sealed class FileLoggerProviderTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "localmind-logs-" + Guid.NewGuid().ToString("N"));
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 11, 13, 20, 15, 0, TimeSpan.FromHours(2)));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Log_WarningAndError_WrittenToDailyFile_InformationSkipped()
    {
        using (var provider = new FileLoggerProvider(_directory, "app", timeProvider: _clock))
        {
            var logger = provider.CreateLogger("LocalMind.Test");
            Write(logger, LogLevel.Information, "не має потрапити");
            Write(logger, LogLevel.Warning, "Файл пошкоджений: a.pdf");
            Write(logger, LogLevel.Error, "Необроблений виняток", new InvalidOperationException("збій"));
        }

        var text = File.ReadAllText(Path.Combine(_directory, "app-2026-11-13.log"));
        Assert.DoesNotContain("не має потрапити", text, StringComparison.Ordinal);
        Assert.Contains("[WRN] LocalMind.Test: Файл пошкоджений: a.pdf", text, StringComparison.Ordinal);
        Assert.Contains("[ERR] LocalMind.Test: Необроблений виняток", text, StringComparison.Ordinal);
        Assert.Contains("System.InvalidOperationException: збій", text, StringComparison.Ordinal);
        Assert.StartsWith("2026-11-13 20:15:00.000 +02:00", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Log_NothingAboveMinimum_CreatesNoFiles()
    {
        using (var provider = new FileLoggerProvider(_directory, "app", timeProvider: _clock))
        {
            Write(provider.CreateLogger("x"), LogLevel.Information, "інфо");
        }

        Assert.False(Directory.Exists(_directory));
    }

    [Fact]
    public void Log_NextDay_StartsNewFile()
    {
        using (var provider = new FileLoggerProvider(_directory, "cli", timeProvider: _clock))
        {
            var logger = provider.CreateLogger("x");
            Write(logger, LogLevel.Warning, "перший день");
            _clock.Now = _clock.Now.AddDays(1);
            Write(logger, LogLevel.Warning, "другий день");
        }

        Assert.Contains("перший день", File.ReadAllText(Path.Combine(_directory, "cli-2026-11-13.log")), StringComparison.Ordinal);
        Assert.Contains("другий день", File.ReadAllText(Path.Combine(_directory, "cli-2026-11-14.log")), StringComparison.Ordinal);
    }

    [Fact]
    public void FirstWrite_DeletesOnlyOwnFilesOlderThanRetention()
    {
        Directory.CreateDirectory(_directory);
        var old = Path.Combine(_directory, "app-2026-10-01.log");
        var recent = Path.Combine(_directory, "app-2026-11-10.log");
        var otherPrefix = Path.Combine(_directory, "cli-2026-10-01.log");
        var crash = Path.Combine(_directory, "crash-20261001-120000-000.txt");
        foreach (var file in new[] { old, recent, otherPrefix, crash })
        {
            File.WriteAllText(file, "x");
        }

        using (var provider = new FileLoggerProvider(_directory, "app", retainDays: 14, timeProvider: _clock))
        {
            Write(provider.CreateLogger("x"), LogLevel.Warning, "запис");
        }

        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));
        Assert.True(File.Exists(otherPrefix));
        Assert.True(File.Exists(crash));
    }

    [Fact]
    public async Task Log_FromManyThreads_KeepsEveryLineWhole()
    {
        using (var provider = new FileLoggerProvider(_directory, "app", timeProvider: _clock))
        {
            var logger = provider.CreateLogger("x");
            await Task.WhenAll(Enumerable.Range(0, 8).Select(thread => Task.Run(() =>
            {
                for (var i = 0; i < 50; i++)
                {
                    Write(logger, LogLevel.Warning, $"потік {thread} запис {i}");
                }
            })));
        }

        var lines = File.ReadAllLines(Path.Combine(_directory, "app-2026-11-13.log"));
        Assert.Equal(400, lines.Length);
        Assert.All(lines, line => Assert.Matches(@"\[WRN\] x: потік \d запис \d+$", line));
    }

    [Fact]
    public void Log_AfterDispose_IsIgnored()
    {
        var provider = new FileLoggerProvider(_directory, "app", timeProvider: _clock);
        var logger = provider.CreateLogger("x");
        provider.Dispose();

        Write(logger, LogLevel.Error, "після закриття");

        Assert.False(Directory.Exists(_directory));
    }

    [Fact]
    public void Log_FileLockedExclusively_DoesNotThrow()
    {
        Directory.CreateDirectory(_directory);
        using var locked = new FileStream(Path.Combine(_directory, "app-2026-11-13.log"), FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        using var provider = new FileLoggerProvider(_directory, "app", timeProvider: _clock);

        var exception = Record.Exception(() => Write(provider.CreateLogger("x"), LogLevel.Error, "запис"));

        Assert.Null(exception);
    }

    [Fact]
    public void Constructor_InvalidArguments_Throw()
    {
        Assert.Throws<ArgumentException>(() => new FileLoggerProvider(" ", "app"));
        Assert.Throws<ArgumentException>(() => new FileLoggerProvider(_directory, ""));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FileLoggerProvider(_directory, "app", retainDays: 0));
    }

    /// <summary>Пряме <see cref="ILogger.Log"/>: тести перевіряють провайдер, а не шаблони повідомлень.</summary>
    internal static void Write(ILogger logger, LogLevel level, string message, Exception? exception = null) =>
        logger.Log(level, default, message, exception, (text, _) => text);

    internal sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();

        public override TimeZoneInfo LocalTimeZone { get; } =
            TimeZoneInfo.CreateCustomTimeZone("test+2", now.Offset, "test+2", "test+2");
    }
}
