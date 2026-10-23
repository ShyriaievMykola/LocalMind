using LocalMind.Core.Errors;

namespace LocalMind.Core.Tests.Errors;

public class LocalMindExceptionTests
{
    [Fact]
    public void Constructor_Default_HasMessage()
    {
        Assert.False(string.IsNullOrEmpty(new LocalMindException().Message));
    }

    [Fact]
    public void Constructor_Message_SetsMessage()
    {
        Assert.Equal("Модель не знайдено", new LocalMindException("Модель не знайдено").Message);
    }

    [Fact]
    public void Constructor_MessageAndInner_SetsBoth()
    {
        var inner = new IOException("disk");

        var exception = new LocalMindException("Не вдалося відкрити базу", inner);

        Assert.Equal("Не вдалося відкрити базу", exception.Message);
        Assert.Same(inner, exception.InnerException);
    }
}
