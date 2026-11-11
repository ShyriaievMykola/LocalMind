using LocalMind.App.ViewModels;
using LocalMind.Core.Models;
using LocalMind.TestUtilities;
using Microsoft.Extensions.Logging.Abstractions;

namespace LocalMind.App.Tests;

public sealed class ResultCardViewModelTests
{
    private readonly FakeFileLauncher _files = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "localmind-notes");

    [Fact]
    public void Constructor_PreservesSnippetAndScore_AndShowsRelativePath()
    {
        var result = Result(Path.Combine(_folder, "універ", "конспект.md"));
        var card = Create(result, [_folder]);

        Assert.Same(result, card.Result);
        Assert.Same(result.Snippet, card.Snippet);
        Assert.Equal("конспект.md", card.FileName);
        Assert.Equal(Path.Combine("універ", "конспект.md"), card.RelativePath);
        Assert.Equal(result.DocumentPath, card.DocumentPath);
        Assert.Equal(result.Score, card.Score);
    }

    [Fact]
    public void Constructor_NestedFolders_UsesClosestSourceFolder()
    {
        var nested = Path.Combine(_folder, "універ");
        var card = Create(Result(Path.Combine(nested, "лекції", "конспект.md")), [_folder, nested]);

        Assert.Equal(Path.Combine("лекції", "конспект.md"), card.RelativePath);
    }

    [Fact]
    public void Constructor_FolderWithTrailingSeparator_StillShowsRelativePath()
    {
        var card = Create(Result(Path.Combine(_folder, "note.md")), [_folder + Path.DirectorySeparatorChar]);

        Assert.Equal("note.md", card.RelativePath);
    }

    [Fact]
    public void Constructor_SiblingWithSamePrefix_DoesNotTreatItAsParent()
    {
        var result = Result(Path.Combine(_folder + "-other", "note.md"));
        var card = Create(result, [_folder]);

        Assert.Equal(result.DocumentPath, card.RelativePath);
    }

    [Fact]
    public void Constructor_NoMatchingFolder_ShowsOriginalPath()
    {
        var result = Result(Path.Combine(_folder, "note.md"));

        Assert.Equal(result.DocumentPath, Create(result, []).RelativePath);
    }

    [Fact]
    public void Constructor_FilesystemRoot_IsAValidSourceFolder()
    {
        var path = Path.Combine(_folder, "note.md");
        var root = Path.GetPathRoot(path)!;

        Assert.Equal(Path.GetRelativePath(root, path), Create(Result(path), [root]).RelativePath);
    }

    [Fact]
    public async Task OpenSource_PassesOriginalFullPath_ToInjectedService()
    {
        var result = Result(Path.Combine(_folder, "папка з пробілами", "конспект & план.md"));
        var card = Create(result, [_folder]);

        await card.OpenSourceCommand.ExecuteAsync(null);

        Assert.Equal(result.DocumentPath, Assert.Single(_files.Paths));
        Assert.NotEqual(card.RelativePath, Assert.Single(_files.Paths));
        Assert.False(card.HasError);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OpenSource_Failure_ShowsErrorAndAllowsRetry(bool missing)
    {
        var card = Create(Result(Path.Combine(_folder, "note.md")), [_folder]);
        _files.Handler = (_, _) => throw (missing ? new FileNotFoundException() : new InvalidOperationException());

        await card.OpenSourceCommand.ExecuteAsync(null);

        Assert.Equal(missing ? "Файл не знайдено: note.md" : "Не вдалося відкрити файл системною програмою.", card.ErrorMessage);

        _files.Handler = (_, _) => Task.CompletedTask;
        await card.OpenSourceCommand.ExecuteAsync(null);

        Assert.Equal(2, _files.Paths.Count);
        Assert.Null(card.ErrorMessage);
        Assert.False(card.HasError);
    }

    private ResultCardViewModel Create(SearchResult result, IReadOnlyList<string> folders) =>
        new(result, folders, _files, NullLogger<ResultCardViewModel>.Instance);

    private static SearchResult Result(string path) =>
        new(TestData.Chunk(Guid.NewGuid(), text: "початок чай та кава кінець"), path, 0.123456,
            new Snippet("чай та кава", 8, [new(0, 3), new(7, 4)], hasTextBefore: true, hasTextAfter: true));
}
