namespace LocalMind.App.Services;

/// <summary>Відкриває локальний файл програмою, призначеною операційною системою.</summary>
public interface IFileLauncher
{
    Task OpenAsync(string path, CancellationToken cancellationToken = default);
}
