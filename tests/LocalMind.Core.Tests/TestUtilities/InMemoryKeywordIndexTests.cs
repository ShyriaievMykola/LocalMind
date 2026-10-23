using LocalMind.TestUtilities;

namespace LocalMind.Core.Tests.TestUtilities;

public class InMemoryKeywordIndexTests
{
    private static async Task<(InMemoryKeywordIndex Index, IReadOnlyList<Core.Models.Chunk> Chunks)> CreateAsync(
        params string[] texts)
    {
        var repository = new InMemoryDocumentRepository();
        var document = TestData.Document();
        var chunks = TestData.Chunks(document.Id, texts);
        await repository.SaveAsync(
            document, chunks, [.. chunks.Select(_ => (ReadOnlyMemory<float>)new float[] { 1 })], "m");

        return (new InMemoryKeywordIndex(repository), chunks);
    }

    [Fact]
    public async Task Search_RanksByNumberOfMatchedTerms()
    {
        var (index, chunks) = await CreateAsync(
            "налаштування docker мережі",
            "docker compose і bridge мережа",
            "рецепт борщу");

        var results = await index.SearchAsync("Docker bridge", 10);

        Assert.Equal([chunks[1].Id, chunks[0].Id], results.Select(r => r.ChunkId));
        Assert.Equal([2.0, 1.0], results.Select(r => r.Score));
    }

    [Fact]
    public async Task Search_RespectsK()
    {
        var (index, _) = await CreateAsync("ssh ключ", "ssh сервер", "ssh порт");

        Assert.Equal(2, (await index.SearchAsync("ssh", 2)).Count);
    }

    [Fact]
    public async Task Search_NoMatches_ReturnsEmpty()
    {
        var (index, _) = await CreateAsync("docker");

        Assert.Empty(await index.SearchAsync("qwxyz", 5));
    }

    [Fact]
    public async Task Search_SeesChunksSavedAfterCreation()
    {
        var repository = new InMemoryDocumentRepository();
        var index = new InMemoryKeywordIndex(repository);
        var document = TestData.Document();
        await repository.SaveAsync(document, TestData.Chunks(document.Id, "новий фрагмент"), [new float[] { 1 }], "m");

        Assert.Single(await index.SearchAsync("новий", 5));
    }

    [Theory]
    [InlineData("", 5)]
    [InlineData("  ", 5)]
    public async Task Search_BlankQuery_Throws(string query, int k)
    {
        var (index, _) = await CreateAsync("text");

        await Assert.ThrowsAsync<ArgumentException>(() => index.SearchAsync(query, k));
    }

    [Fact]
    public async Task Search_NonPositiveK_Throws()
    {
        var (index, _) = await CreateAsync("text");

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => index.SearchAsync("text", 0));
    }
}
