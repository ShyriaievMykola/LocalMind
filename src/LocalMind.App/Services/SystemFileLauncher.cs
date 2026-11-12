using System.Diagnostics;
using LocalMind.Core.Errors;
using LocalMind.Core.Ingestion.Parsers;

namespace LocalMind.App.Services;

/// <summary>Системне відкриття документа через shell association на Windows, Linux і macOS.</summary>
public sealed class SystemFileLauncher : IFileLauncher
{
    private readonly Action<ProcessStartInfo> _start;
    private readonly ParserRegistry _parsers;

    public SystemFileLauncher(ParserRegistry parsers) : this(parsers, info => Process.Start(info)?.Dispose())
    {
    }

    internal SystemFileLauncher(ParserRegistry parsers, Action<ProcessStartInfo> start)
    {
        ArgumentNullException.ThrowIfNull(parsers);
        ArgumentNullException.ThrowIfNull(start);
        _parsers = parsers;
        _start = start;
    }

    public Task OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);

        // Перевірка файла та виклик системної програми теж можуть чекати на повільний диск.
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Шлях із БД може бути підмінено: дозволяємо тільки формати зареєстрованих парсерів.
            if (!_parsers.TryGetParser(fullPath, out _))
            {
                throw new LocalMindException("Цей формат файла не підтримується.");
            }

            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException("Файл не знайдено.", fullPath);
            }

            _start(new ProcessStartInfo
            {
                FileName = fullPath,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(fullPath)!,
            });
        }, cancellationToken);
    }
}
