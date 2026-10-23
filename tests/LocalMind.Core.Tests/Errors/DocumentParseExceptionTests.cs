using LocalMind.Core.Errors;

namespace LocalMind.Core.Tests.Errors;

public class DocumentParseExceptionTests
{
    [Fact]
    public void IsLocalMindException()
    {
        Assert.IsAssignableFrom<LocalMindException>(new DocumentParseException());
    }

    [Fact]
    public void Constructor_Message_SetsMessage()
    {
        var exception = new DocumentParseException("Файл зашифровано");

        Assert.Equal("Файл зашифровано", exception.Message);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void Constructor_MessageAndInner_SetsBoth()
    {
        var inner = new InvalidDataException("bad xref table");

        var exception = new DocumentParseException("Файл пошкоджено", inner);

        Assert.Equal("Файл пошкоджено", exception.Message);
        Assert.Same(inner, exception.InnerException);
    }
}
