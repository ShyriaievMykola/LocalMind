using System.Collections.Concurrent;
using LocalMind.App.Services;

namespace LocalMind.App.Tests;

internal sealed class FakeFileLauncher : IFileLauncher
{
    public ConcurrentQueue<string> Paths { get; } = new();

    public Func<string, CancellationToken, Task> Handler { get; set; } = (_, _) => Task.CompletedTask;

    public Task OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        Paths.Enqueue(path);
        return Handler(path, cancellationToken);
    }
}
