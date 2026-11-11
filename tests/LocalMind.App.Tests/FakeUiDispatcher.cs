using System.Collections.Concurrent;
using LocalMind.App.Services;

namespace LocalMind.App.Tests;

/// <summary>
/// UI-потік для тестів: дії лише накопичуються в черзі, а <see cref="RunPending"/> виконує їх у потоці тесту.
/// Так видно, що обробник фонової події сам нічого не змінює у ViewModel.
/// </summary>
internal sealed class FakeUiDispatcher : IUiDispatcher
{
    private readonly ConcurrentQueue<Action> _pending = new();

    public int PendingCount => _pending.Count;

    public void Post(Action action) => _pending.Enqueue(action);

    public void RunPending()
    {
        while (_pending.TryDequeue(out var action))
        {
            action();
        }
    }
}