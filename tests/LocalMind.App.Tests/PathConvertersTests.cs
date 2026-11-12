using System.Globalization;
using LocalMind.App.Converters;

namespace LocalMind.App.Tests;

public sealed class PathConvertersTests
{
    [Fact]
    public void FileName_ReturnsLastSegmentOfPath()
    {
        var path = Path.Combine("notes", "sub", "plan.md");

        var result = PathConverters.FileName.Convert(path, typeof(string), null, CultureInfo.InvariantCulture);

        Assert.Equal("plan.md", result);
    }

    [Fact]
    public void FileName_NullOrEmpty_GivesEmptyText()
    {
        Assert.Equal(string.Empty, PathConverters.FileName.Convert(null, typeof(string), null, CultureInfo.InvariantCulture));
        Assert.Equal(string.Empty, PathConverters.FileName.Convert(string.Empty, typeof(string), null, CultureInfo.InvariantCulture));
    }
}
