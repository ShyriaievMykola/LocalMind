using Avalonia.Controls;
using LocalMind.App.ViewModels;

namespace LocalMind.App.Tests;

#pragma warning disable IL2026 // ViewLocator позначено RequiresUnreferencedCode, у тестах обрізання немає.
public sealed class ViewLocatorTests
{
    private readonly ViewLocator _locator = new();

    [Fact]
    public void Match_OnlyViewModels()
    {
        Assert.True(_locator.Match(new NoViewViewModel()));
        Assert.False(_locator.Match("рядок"));
        Assert.False(_locator.Match(null));
    }

    [Fact]
    public void Build_Null_ReturnsNull() => Assert.Null(_locator.Build(null));

    [Fact]
    public void Build_ViewModelWithoutView_ShowsNotFoundText()
    {
        var control = _locator.Build(new NoViewViewModel());

        var text = Assert.IsType<TextBlock>(control);
        Assert.Contains("Not Found", text.Text, StringComparison.Ordinal);
        Assert.Contains("NoViewView", text.Text, StringComparison.Ordinal);
    }

    private sealed class NoViewViewModel : ViewModelBase;
}
#pragma warning restore IL2026
