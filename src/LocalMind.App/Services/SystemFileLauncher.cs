using System.Diagnostics;

namespace LocalMind.App.Services;

/// <summary>Системне відкриття документа через shell association на Windows, Linux і macOS.</summary>
public sealed class SystemFileLauncher : IFileLauncher
{
    private readonly Action<ProcessStartInfo> _start;

    public SystemFileLauncher() : this(info => Process.Start(info)?.Dispose())
    {
    }

    internal SystemFileLauncher(Action<ProcessStartInfo> start) => _start = start;

    public Task OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);

        // Перевірка файла та виклик системної програми теж можуть чекати на повільний диск.
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
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
