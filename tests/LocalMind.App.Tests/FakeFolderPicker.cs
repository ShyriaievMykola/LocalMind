using LocalMind.App.Services;

namespace LocalMind.App.Tests;

/// <summary>Вибір папки без вікна: повертає заданий шлях.</summary>
internal sealed class FakeFolderPicker : IFolderPicker
{
    /// <summary>Що «обере» користувач; <see langword="null"/> — скасував вибір.</summary>
    public string? Result { get; set; }

    public int Calls { get; private set; }

    /// <summary>Якщо задано — вибір завершується цим винятком.</summary>
    public Exception? Error { get; set; }

    public Task<string?> PickFolderAsync()
    {
        Calls++;
        return Error is null ? Task.FromResult(Result) : Task.FromException<string?>(Error);
    }
}