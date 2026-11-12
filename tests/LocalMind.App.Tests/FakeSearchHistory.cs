using LocalMind.App.Services;

namespace LocalMind.App.Tests;

/// <summary>Історія в пам'яті: та сама поведінка «новий нагору, без дублів», без файла.</summary>
internal sealed class FakeSearchHistory : ISearchHistory
{
    public List<string> Entries { get; } = [];

    public IReadOnlyList<string> Items => [.. Entries];

    public Task AddAsync(string query, CancellationToken cancellationToken = default)
    {
        var text = query.Trim();
        Entries.RemoveAll(item => item.Equals(text, StringComparison.OrdinalIgnoreCase));
        Entries.Insert(0, text);
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        Entries.Clear();
        return Task.CompletedTask;
    }
}