using LocalMind.Core.Models;
using LocalMind.Core.Storage;
using LocalMind.Core.Storage.FullText;
using LocalMind.TestUtilities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LocalMind.Core.Tests.Storage;

public sealed class Fts5KeywordIndexTests : IAsyncLifetime, IDisposable
{
    private const string Model = "test-model";

    private readonly SqliteConnection _connection = new("Data Source=:memory:;Foreign Keys=True");
    private TestContextFactory _factory = null!;
    private DocumentRepository _repository = null!;
    private Fts5KeywordIndex _index = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<LocalMindDbContext>().UseSqlite(_connection).Options;
        _factory = new TestContextFactory(options);
        await using var context = _factory.CreateDbContext();
        await context.Database.MigrateAsync();
        _repository = new DocumentRepository(_factory);
        _index = new Fts5KeywordIndex(_factory);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task Search_FindsSavedChunk_BestMatchFirst()
    {
        var chunks = await SaveAsync("/notes/a.md", "кава зранку", "чай увечері", "кава і цукор зранку");

        var results = await _index.SearchAsync("кава цукор", 10);

        Assert.Equal([chunks[2].Id, chunks[0].Id], results.Select(r => r.ChunkId));
        Assert.True(results[0].Score > results[1].Score);
    }

    [Fact]
    public async Task Search_RespectsK()
    {
        await SaveAsync("/notes/a.md", "кава один", "кава два", "кава три");

        Assert.Equal(2, (await _index.SearchAsync("кава", 2)).Count);
    }

    [Fact]
    public async Task Search_CyrillicQuery_IgnoresCaseAndMatchesWordForms()
    {
        var chunks = await SaveAsync("/notes/a.md", "Випив КАВУ в нотатках про Docker", "інше");

        var results = await _index.SearchAsync("кава НОТАТКИ docker", 10);

        Assert.Equal(chunks[0].Id, Assert.Single(results).ChunkId);
    }

    [Fact]
    public async Task Search_DoesNotMergeLettersWithDiacritics()
    {
        await SaveAsync("/notes/a.md", "його");

        Assert.Empty(await _index.SearchAsync("иого", 10));
    }

    [Fact]
    public async Task Index_FollowsDeleteAndReindex()
    {
        var document = TestData.Document("/notes/a.md");
        await _repository.SaveAsync(document, TestData.Chunks(document.Id, "стара кава"), Vectors(1), Model);

        var reindexed = TestData.Document("/notes/a.md");
        var newChunks = TestData.Chunks(reindexed.Id, "новий чай");
        await _repository.SaveAsync(reindexed, newChunks, Vectors(1), Model);

        Assert.Empty(await _index.SearchAsync("кава", 10));
        Assert.Equal(newChunks[0].Id, Assert.Single(await _index.SearchAsync("чай", 10)).ChunkId);

        await _repository.DeleteAsync(reindexed.Id);

        Assert.Empty(await _index.SearchAsync("чай", 10));
    }

    [Fact]
    public async Task Index_FollowsUpdateOfChunkText()
    {
        var chunks = await SaveAsync("/notes/a.md", "кава");
        await using (var context = _factory.CreateDbContext())
        {
            await context.Chunks.Where(c => c.Id == chunks[0].Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(c => c.Text, "чай"));
        }

        Assert.Empty(await _index.SearchAsync("кава", 10));
        Assert.Single(await _index.SearchAsync("чай", 10));
    }

    [Fact]
    public async Task Migration_IndexesChunksSavedBeforeIt()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        var factory = new TestContextFactory(new DbContextOptionsBuilder<LocalMindDbContext>().UseSqlite(connection).Options);
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.MigrateAsync("20261108133909_InitialCreate");
        }

        var document = TestData.Document();
        var chunks = TestData.Chunks(document.Id, "давня кава");
        await new DocumentRepository(factory).SaveAsync(document, chunks, Vectors(1), Model);

        await using (var context = factory.CreateDbContext())
        {
            await context.Database.MigrateAsync();
        }

        Assert.Equal(chunks[0].Id, Assert.Single(await new Fts5KeywordIndex(factory).SearchAsync("кава", 10)).ChunkId);
    }

    [Theory]
    [InlineData("\"")]
    [InlineData("\"кава")]
    [InlineData("кава*")]
    [InlineData("*")]
    [InlineData("кава OR чай")]
    [InlineData("NEAR(кава чай)")]
    [InlineData("кава AND NOT чай")]
    [InlineData("'; DROP TABLE Chunks; --")]
    [InlineData("Text:кава")]
    [InlineData("^кава")]
    [InlineData("(кава")]
    [InlineData("{кава}")]
    [InlineData("-кава +чай")]
    public async Task Search_FtsSyntaxInQuery_DoesNotThrowAndKeepsTable(string query)
    {
        await SaveAsync("/notes/a.md", "кава і чай");

        var results = await _index.SearchAsync(query, 10);

        Assert.True(results.Count <= 1);
        Assert.Single(await _index.SearchAsync("кава", 10));
    }

    [Fact]
    public async Task Search_OnlyStopWordsOrPunctuation_ReturnsEmpty()
    {
        await SaveAsync("/notes/a.md", "кава і чай");

        Assert.Empty(await _index.SearchAsync("і та на", 10));
        Assert.Empty(await _index.SearchAsync("!!! ...", 10));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Search_BlankQuery_Throws(string query)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _index.SearchAsync(query, 10));
    }

    [Fact]
    public async Task Search_NonPositiveK_Throws()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _index.SearchAsync("кава", 0));
    }

    [Fact]
    public void BuildMatchExpression_QuotesStemsAndJoinsWithOr()
    {
        Assert.Equal("\"кав\"* OR \"нотат\"*", Fts5KeywordIndex.BuildMatchExpression("Кава і \"нотатки\""));
    }

    private async Task<IReadOnlyList<Chunk>> SaveAsync(string path, params string[] texts)
    {
        var document = TestData.Document(path);
        var chunks = TestData.Chunks(document.Id, texts);
        await _repository.SaveAsync(document, chunks, Vectors(chunks.Count), Model);
        return chunks;
    }

    private static List<ReadOnlyMemory<float>> Vectors(int count) =>
        [.. Enumerable.Range(0, count).Select(i => new ReadOnlyMemory<float>([i, 1f]))];

    private sealed class TestContextFactory(DbContextOptions<LocalMindDbContext> options)
        : IDbContextFactory<LocalMindDbContext>
    {
        public LocalMindDbContext CreateDbContext() => new(options);
    }
}
