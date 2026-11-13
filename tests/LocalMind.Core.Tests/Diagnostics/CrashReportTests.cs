using LocalMind.Core.Diagnostics;

namespace LocalMind.Core.Tests.Diagnostics;

public sealed class CrashReportTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "localmind-crash-" + Guid.NewGuid().ToString("N"));
    private readonly FileLoggerProviderTests.ManualClock _clock = new(new DateTimeOffset(2026, 11, 13, 20, 15, 1, 234, TimeSpan.FromHours(2)));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void TryWrite_CreatesReportWithSourceEnvironmentAndStack()
    {
        var exception = Throw();

        var path = CrashReport.TryWrite(_directory, exception, "UI-потік", _clock);

        Assert.Equal(Path.Combine(_directory, "crash-20261113-201501-234.txt"), path);
        var text = File.ReadAllText(path!);
        Assert.Contains("LocalMind: звіт про аварію", text, StringComparison.Ordinal);
        Assert.Contains("Джерело:   UI-потік", text, StringComparison.Ordinal);
        Assert.Contains("ОС:", text, StringComparison.Ordinal);
        Assert.Contains(".NET:", text, StringComparison.Ordinal);
        Assert.Contains("System.InvalidOperationException: зламалось", text, StringComparison.Ordinal);
        Assert.Contains(nameof(Throw), text, StringComparison.Ordinal);
    }

    [Fact]
    public void TryWrite_SameMillisecond_DoesNotOverwrite()
    {
        var first = CrashReport.TryWrite(_directory, new InvalidOperationException("1"), "a", _clock);
        var second = CrashReport.TryWrite(_directory, new InvalidOperationException("2"), "b", _clock);

        Assert.NotEqual(first, second);
        Assert.EndsWith("-2.txt", second, StringComparison.Ordinal);
        Assert.Contains("Джерело:   a", File.ReadAllText(first!), StringComparison.Ordinal);
        Assert.Contains("Джерело:   b", File.ReadAllText(second!), StringComparison.Ordinal);
    }

    [Fact]
    public void TryWrite_DirectoryIsFile_ReturnsNullWithoutThrowing()
    {
        File.WriteAllText(_directory, "не тека");
        try
        {
            Assert.Null(CrashReport.TryWrite(_directory, new InvalidOperationException(), "x", _clock));
        }
        finally
        {
            File.Delete(_directory);
        }
    }

    private static InvalidOperationException Throw()
    {
        try
        {
            throw new InvalidOperationException("зламалось");
        }
        catch (InvalidOperationException exception)
        {
            return exception;
        }
    }
}
