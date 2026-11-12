using LocalMind.App.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace LocalMind.App.Tests;

public sealed class SearchHistoryStoreTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("localmind-history-").FullName;

    private string HistoryPath => Path.Combine(_folder, "nested", "search-history.json");

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public async Task Add_PutsNewestFirst_AndMovesRepeatToTop()
    {
        using var store = CreateStore();

        await store.AddAsync("кава");
        await store.AddAsync("чай");
        await store.AddAsync("  КАВА ");

        Assert.Equal(["КАВА", "чай"], store.Items);
    }

    [Fact]
    public async Task Add_KeepsAtMostTwentyQueries()
    {
        using var store = CreateStore();

        for (var i = 1; i <= 25; i++)
        {
            await store.AddAsync($"запит {i}");
        }

        Assert.Equal(SearchHistoryStore.MaxItems, store.Items.Count);
        Assert.Equal("запит 25", store.Items[0]);
    }

    [Fact]
    public async Task Add_BlankQuery_IsIgnored()
    {
        using var store = CreateStore();

        await store.AddAsync("   ");

        Assert.Empty(store.Items);
    }

    [Fact]
    public async Task Add_TooLongQuery_IsIgnored()
    {
        using var store = CreateStore();
        var longest = new string('б', SearchHistoryStore.MaxQueryLength);

        await store.AddAsync(new string('а', SearchHistoryStore.MaxQueryLength + 1));
        await store.AddAsync(longest);

        Assert.Equal([longest], store.Items);
    }

    [Fact]
    public async Task ParallelAdds_FileAlwaysMatchesItems()
    {
        using var store = CreateStore();

        // Гонка проявлялася не щоразу, тому повторюємо багато раундів.
        for (var round = 0; round < 50; round++)
        {
            await Task.WhenAll(Enumerable.Range(0, 8)
                .Select(i => Task.Run(() => store.AddAsync($"запит {round}-{i}"))));

            using var reloaded = CreateStore();
            Assert.Equal(store.Items, reloaded.Items);
        }
    }

    [Fact]
    public async Task History_SurvivesRestart()
    {
        using (var first = CreateStore())
        {
            await first.AddAsync("кава");
            await first.AddAsync("чай");
        }

        using var second = CreateStore();

        Assert.Equal(["чай", "кава"], second.Items);
    }

    [Fact]
    public async Task Clear_EmptiesHistory_AlsoAfterRestart()
    {
        using (var first = CreateStore())
        {
            await first.AddAsync("кава");
            await first.ClearAsync();

            Assert.Empty(first.Items);
        }

        using var second = CreateStore();

        Assert.Empty(second.Items);
    }

    [Fact]
    public void CorruptedFile_StartsWithEmptyHistory()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(HistoryPath)!);
        File.WriteAllText(HistoryPath, "{ це не json");

        using var store = CreateStore();

        Assert.Empty(store.Items);
    }

    private SearchHistoryStore CreateStore() =>
        new(new SearchHistoryFile(HistoryPath), NullLogger<SearchHistoryStore>.Instance);
}