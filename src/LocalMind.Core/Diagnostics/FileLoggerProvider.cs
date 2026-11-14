using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace LocalMind.Core.Diagnostics;

/// <summary>
/// Пише журнал у файли <c>{prefix}-yyyy-MM-dd.log</c> у теці <c>logs</c>: новий файл щодня, файли, старші за
/// <c>retainDays</c> днів, видаляються під час першого запису. За замовчуванням пише лише попередження й помилки, тож
/// після прогону сценарію лог-файл показує, чи були збої (критерій C2). Збій запису в лог назовні не виходить:
/// журнал не повинен ламати застосунок.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly Lock _gate = new();
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new(StringComparer.Ordinal);
    private readonly string _directory;
    private readonly string _prefix;
    private readonly LogLevel _minimumLevel;
    private readonly int _retainDays;
    private readonly TimeProvider _time;
    private StreamWriter? _writer;
    private DateOnly _writerDate;
    private bool _cleanedUp;
    private bool _disposed;

    /// <param name="directory">Тека журналів; створюється під час першого запису.</param>
    /// <param name="prefix">Початок назви файла: <c>app</c>, <c>cli</c>.</param>
    /// <param name="minimumLevel">Найнижчий рівень, що потрапляє у файл.</param>
    /// <param name="retainDays">Скільки днів зберігати старі файли.</param>
    /// <param name="timeProvider">Годинник; тести передають власний.</param>
    public FileLoggerProvider(
        string directory,
        string prefix,
        LogLevel minimumLevel = LogLevel.Warning,
        int retainDays = 14,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(retainDays);

        _directory = directory;
        _prefix = prefix;
        _minimumLevel = minimumLevel;
        _retainDays = retainDays;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Тека, куди пишуться файли.</summary>
    public string Directory => _directory;

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new FileLogger(this, name));

    /// <summary>Шлях до файла журналу за вказаний день.</summary>
    public string PathFor(DateOnly date) =>
        Path.Combine(_directory, string.Create(CultureInfo.InvariantCulture, $"{_prefix}-{date:yyyy-MM-dd}.log"));

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _writer?.Dispose();
            _writer = null;
        }
    }

    private bool IsEnabled(LogLevel level) => level != LogLevel.None && level >= _minimumLevel;

    private void Write(LogLevel level, string category, string message, Exception? exception)
    {
        var now = _time.GetLocalNow();
        var entry = Format(now, level, category, message, exception);

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                var writer = WriterFor(DateOnly.FromDateTime(now.DateTime));
                writer.Write(entry);
                writer.Flush();
            }
            catch (IOException)
            {
                // Диск повний або файл заблоковано: журнал втрачає запис, застосунок працює далі.
            }
            catch (UnauthorizedAccessException)
            {
                // Немає прав на теку журналів — те саме.
            }
        }
    }

    private StreamWriter WriterFor(DateOnly date)
    {
        if (_writer is not null && _writerDate == date)
        {
            return _writer;
        }

        _writer?.Dispose();
        _writer = null;

        System.IO.Directory.CreateDirectory(_directory);
        if (!_cleanedUp)
        {
            _cleanedUp = true;
            DeleteOldFiles(date);
        }

        // FileShare.ReadWrite: журнал можна відкрити в редакторі, поки застосунок працює.
        var stream = new FileStream(PathFor(date), FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        _writer = new StreamWriter(stream, Utf8NoBom);
        _writerDate = date;
        return _writer;
    }

    private void DeleteOldFiles(DateOnly today)
    {
        var oldest = today.AddDays(-_retainDays);
        foreach (var file in System.IO.Directory.EnumerateFiles(_directory, _prefix + "-*.log"))
        {
            var datePart = Path.GetFileNameWithoutExtension(file)[(_prefix.Length + 1)..];
            if (DateOnly.TryParseExact(datePart, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                && date < oldest)
            {
                try
                {
                    File.Delete(file);
                }
                catch (IOException)
                {
                    // Файл відкритий іншим процесом: видалимо наступного разу.
                }
                catch (UnauthorizedAccessException)
                {
                    // Без прав старий файл лишається, це не заважає писати новий.
                }
            }
        }
    }

    internal static string Format(DateTimeOffset time, LogLevel level, string category, string message, Exception? exception)
    {
        var builder = new StringBuilder();
        builder.Append(CultureInfo.InvariantCulture, $"{time:yyyy-MM-dd HH:mm:ss.fff zzz} [{ShortName(level)}] {category}: {message}");
        builder.AppendLine();
        if (exception is not null)
        {
            builder.AppendLine(exception.ToString());
        }

        return builder.ToString();
    }

    private static string ShortName(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => level.ToString(),
    };

    private sealed class FileLogger(FileLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => owner.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            if (!IsEnabled(logLevel))
            {
                return;
            }

            owner.Write(logLevel, category, formatter(state, exception), exception);
        }
    }
}
