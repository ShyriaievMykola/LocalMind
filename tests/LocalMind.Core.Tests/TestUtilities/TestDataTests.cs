using LocalMind.Core.Models;
using LocalMind.TestUtilities;

namespace LocalMind.Core.Tests.TestUtilities;

public class TestDataTests
{
    [Fact]
    public void Hash_IsValidAndDependsOnSeed()
    {
        Assert.Equal(Document.ContentHashLength, TestData.Hash().Length);
        Assert.NotEqual(TestData.Hash(1), TestData.Hash(2));
        Assert.Equal(TestData.Hash(7), TestData.Hash(7));
    }

    [Fact]
    public void Document_ErrorStatus_GetsReason()
    {
        var document = TestData.Document(status: DocumentStatus.Error);

        Assert.Equal(DocumentStatus.Error, document.Status);
        Assert.False(string.IsNullOrWhiteSpace(document.Error));
    }

    [Fact]
    public void Chunks_HaveSequentialOrdinalsAndOffsets()
    {
        var documentId = Guid.NewGuid();

        var chunks = TestData.Chunks(documentId, "abc", "de", "f");

        Assert.Equal([0, 1, 2], chunks.Select(c => c.Ordinal));
        Assert.Equal([0, 4, 7], chunks.Select(c => c.StartOffset));
        Assert.All(chunks, c => Assert.Equal(documentId, c.DocumentId));
    }
}
