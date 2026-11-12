using LocalMind.App.Services;

namespace LocalMind.App.Tests;

/// <summary>Вибір папки без вікна: повертає заданий шлях.</summary>
internal sealed class FakeFolderPicker : IFolderPicker
{
    /// <summary>Що «обере» користувач; <see langword="null"/> — скасував вибір.</summary>
    public string? Result { get; set; }

    public int Calls { get; private set; }

    public Task<string?> PickFolderAsync()
    {
        Calls++;
        return Task.FromResult(Result);
    }
}