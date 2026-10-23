using LocalMind.Core.Models;

namespace LocalMind.Core.Tests.Models;

public class DocumentTests
{
    private static readonly string ValidHash = new('a', Document.ContentHashLength);
    private static readonly DateTimeOffset IndexedAt = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_ValidIndexedDocument_SetsProperties()
    {
        var id = Guid.NewGuid();

        var document = new Document(id, "/notes/docker.md", ValidHash, DocumentStatus.Indexed, IndexedAt);

        Assert.Equal(id, document.Id);
        Assert.Equal("/notes/docker.md", document.Path);
        Assert.Equal(ValidHash, document.ContentHash);
        Assert.Equal(DocumentStatus.Indexed, document.Status);
        Assert.Equal(IndexedAt, document.IndexedAt);
        Assert.Null(document.Error);
    }

    [Fact]
    public void Constructor_UppercaseHash_IsNormalizedToLowercase()
    {
        var document = new Document(
            Guid.NewGuid(), "/a.md", new string('A', Document.ContentHashLength), DocumentStatus.Indexed, IndexedAt);

        Assert.Equal(new string('a', Document.ContentHashLength), document.ContentHash);
    }

    [Fact]
    public void Constructor_ErrorStatusWithReason_SetsError()
    {
        var document = new Document(
            Guid.NewGuid(), "/broken.pdf", ValidHash, DocumentStatus.Error, IndexedAt, "Файл пошкоджено");

        Assert.Equal(DocumentStatus.Error, document.Status);
        Assert.Equal("Файл пошкоджено", document.Error);
    }

    [Fact]
    public void Constructor_EmptyId_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => new Document(Guid.Empty, "/a.md", ValidHash, DocumentStatus.Indexed, IndexedAt));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_BlankPath_Throws(string path)
    {
        Assert.Throws<ArgumentException>(
            () => new Document(Guid.NewGuid(), path, ValidHash, DocumentStatus.Indexed, IndexedAt));
    }

    [Fact]
    public void Constructor_NullPath_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new Document(Guid.NewGuid(), null!, ValidHash, DocumentStatus.Indexed, IndexedAt));
    }

    [Fact]
    public void Constructor_NullHash_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new Document(Guid.NewGuid(), "/a.md", null!, DocumentStatus.Indexed, IndexedAt));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc123")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void Constructor_InvalidHash_Throws(string hash)
    {
        Assert.Throws<ArgumentException>(
            () => new Document(Guid.NewGuid(), "/a.md", hash, DocumentStatus.Indexed, IndexedAt));
    }

    [Fact]
    public void Constructor_UndefinedStatus_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Document(Guid.NewGuid(), "/a.md", ValidHash, (DocumentStatus)42, IndexedAt));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_ErrorStatusWithoutReason_Throws(string? error)
    {
        Assert.Throws<ArgumentException>(
            () => new Document(Guid.NewGuid(), "/a.md", ValidHash, DocumentStatus.Error, IndexedAt, error));
    }

    [Theory]
    [InlineData(DocumentStatus.Indexed)]
    [InlineData(DocumentStatus.Empty)]
    [InlineData(DocumentStatus.NoText)]
    public void Constructor_NonErrorStatusWithReason_Throws(DocumentStatus status)
    {
        Assert.Throws<ArgumentException>(
            () => new Document(Guid.NewGuid(), "/a.md", ValidHash, status, IndexedAt, "причина"));
    }

    [Fact]
    public void Equality_SameValues_AreEqual()
    {
        var id = Guid.NewGuid();

        var first = new Document(id, "/a.md", ValidHash, DocumentStatus.Indexed, IndexedAt);
        var second = new Document(id, "/a.md", ValidHash, DocumentStatus.Indexed, IndexedAt);

        Assert.Equal(first, second);
    }
}
