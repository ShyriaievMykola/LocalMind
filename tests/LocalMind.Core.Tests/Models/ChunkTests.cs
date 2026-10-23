using LocalMind.Core.Models;

namespace LocalMind.Core.Tests.Models;

public class ChunkTests
{
    [Fact]
    public void Constructor_ValidArguments_SetsProperties()
    {
        var id = Guid.NewGuid();
        var documentId = Guid.NewGuid();

        var chunk = new Chunk(id, documentId, 2, "bridge-мережа для контейнерів", 128);

        Assert.Equal(id, chunk.Id);
        Assert.Equal(documentId, chunk.DocumentId);
        Assert.Equal(2, chunk.Ordinal);
        Assert.Equal("bridge-мережа для контейнерів", chunk.Text);
        Assert.Equal(128, chunk.StartOffset);
    }

    [Fact]
    public void Constructor_ZeroOrdinalAndOffset_AreAllowed()
    {
        var chunk = new Chunk(Guid.NewGuid(), Guid.NewGuid(), 0, "текст", 0);

        Assert.Equal(0, chunk.Ordinal);
        Assert.Equal(0, chunk.StartOffset);
    }

    [Fact]
    public void Constructor_EmptyId_Throws()
    {
        Assert.Throws<ArgumentException>(() => new Chunk(Guid.Empty, Guid.NewGuid(), 0, "текст", 0));
    }

    [Fact]
    public void Constructor_EmptyDocumentId_Throws()
    {
        Assert.Throws<ArgumentException>(() => new Chunk(Guid.NewGuid(), Guid.Empty, 0, "текст", 0));
    }

    [Fact]
    public void Constructor_NegativeOrdinal_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Chunk(Guid.NewGuid(), Guid.NewGuid(), -1, "текст", 0));
    }

    [Fact]
    public void Constructor_NegativeStartOffset_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Chunk(Guid.NewGuid(), Guid.NewGuid(), 0, "текст", -1));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \n\t ")]
    public void Constructor_BlankText_Throws(string text)
    {
        Assert.Throws<ArgumentException>(() => new Chunk(Guid.NewGuid(), Guid.NewGuid(), 0, text, 0));
    }

    [Fact]
    public void Constructor_NullText_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new Chunk(Guid.NewGuid(), Guid.NewGuid(), 0, null!, 0));
    }
}
