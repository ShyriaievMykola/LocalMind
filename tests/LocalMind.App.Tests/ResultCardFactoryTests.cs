using LocalMind.App.Services;
using LocalMind.App.ViewModels;
using LocalMind.Core.Models;
using LocalMind.TestUtilities;
using Microsoft.Extensions.Logging.Abstractions;

namespace LocalMind.App.Tests;

public sealed class ResultCardFactoryTests
{
    private readonly InMemoryDocumentRepository _repository = new();
    private readonly FakeFileLauncher _files = new();

    [Fact]
    public async Task Create_UsesTrackedFolders_AndKeepsResultOrderAndServices()
    {
        var root = Path.Combine(Path.GetTempPath(), "notes");
        await _repository.AddSourceFolderAsync(root);
        var first = new SearchResult(TestData.Chunk(Guid.NewGuid()), Path.Combine(root, "sub", "one.md"), 0.9);
        var second = new SearchResult(TestData.Chunk(Guid.NewGuid()), Path.Combine(root, "two.md"), 0.7);

        var cards = await Factory().CreateAsync([first, second]);

        Assert.Equal([first, second], cards.Select(c => c.Result));
        Assert.Equal(Path.Combine("sub", "one.md"), cards[0].RelativePath);
        Assert.Equal("two.md", cards[1].RelativePath);
        await cards[0].OpenSourceCommand.ExecuteAsync(null);
        Assert.Equal(first.DocumentPath, Assert.Single(_files.Paths));
    }

    [Fact]
    public async Task Create_AfterFolderListChanges_UsesCurrentFolders()
    {
        var root = Path.Combine(Path.GetTempPath(), "notes");
        var nested = Path.Combine(root, "sub");
        await _repository.AddSourceFolderAsync(root);
        var result = new SearchResult(TestData.Chunk(Guid.NewGuid()), Path.Combine(nested, "one.md"), 0.9);
        var factory = Factory();
        var before = await factory.CreateAsync([result]);
        await _repository.AddSourceFolderAsync(nested);
        var after = await factory.CreateAsync([result]);

        Assert.Equal(Path.Combine("sub", "one.md"), Assert.Single(before).RelativePath);
        Assert.Equal("one.md", Assert.Single(after).RelativePath);
    }

    [Fact]
    public async Task Create_Cancelled_DoesNotPublishCards()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Factory().CreateAsync([], cancellation.Token));
    }

    private ResultCardFactory Factory() => new(_repository, _files, NullLogger<ResultCardViewModel>.Instance);
}
