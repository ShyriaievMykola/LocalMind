using LocalMind.App.ViewModels;
using LocalMind.Core.Models;

namespace LocalMind.App.Tests;

public sealed class LibraryDocumentItemTests
{
    [Theory]
    [InlineData(DocumentStatus.Indexed, "Проіндексовано", false)]
    [InlineData(DocumentStatus.Empty, "Порожній файл", false)]
    [InlineData(DocumentStatus.NoText, "Текст не знайдено", false)]
    [InlineData(DocumentStatus.Error, "Помилка", true)]
    [InlineData((DocumentStatus)999, "Невідомо", false)]
    public void StatusTextAndIsError_FollowStatus(DocumentStatus status, string text, bool isError)
    {
        var item = new LibraryDocumentItem("/notes/a.md", status, 2, null);

        Assert.Equal(text, item.StatusText);
        Assert.Equal(isError, item.IsError);
    }

    [Fact]
    public void FileName_IsLastPathSegment()
    {
        var item = new LibraryDocumentItem(Path.Combine("notes", "sub", "a.md"), DocumentStatus.Indexed, 0, null);

        Assert.Equal("a.md", item.FileName);
    }
}
