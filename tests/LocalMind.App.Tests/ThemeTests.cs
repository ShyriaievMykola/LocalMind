using System.Text.RegularExpressions;
using System.Xml.Linq;
using Avalonia.Styling;
using LocalMind.App.Services;

namespace LocalMind.App.Tests;

public sealed partial class ThemeTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Theory]
    [InlineData(ThemePreference.System)]
    [InlineData(ThemePreference.Light)]
    [InlineData(ThemePreference.Dark)]
    public void Apply_WithoutRunningApp_RemembersPreference(ThemePreference preference)
    {
        var service = new ThemeService();

        service.Apply(preference);

        Assert.Equal(preference, service.Preference);
    }

    [Fact]
    public void ToVariant_MapsPreferences()
    {
        Assert.Equal(ThemeVariant.Light, ThemeService.ToVariant(ThemePreference.Light));
        Assert.Equal(ThemeVariant.Dark, ThemeService.ToVariant(ThemePreference.Dark));
        Assert.Equal(ThemeVariant.Default, ThemeService.ToVariant(ThemePreference.System));
    }

    [Theory]
    [InlineData("Colors.axaml")]
    [InlineData("Brushes.axaml")]
    public void ThemeDictionaries_DarkAndLightDefineSameKeys(string file)
    {
        var (dark, light) = ReadVariants(file);

        Assert.NotEmpty(dark);
        Assert.Equal(dark, light);
    }

    [Fact]
    public void Brushes_CoverEveryColor()
    {
        var colors = ReadVariants("Colors.axaml").Dark.Select(key => key.Replace("Lm.Color.", "", StringComparison.Ordinal));
        var brushes = ReadVariants("Brushes.axaml").Dark.Select(key => key.Replace("Lm.Brush.", "", StringComparison.Ordinal));

        Assert.Equal(colors, brushes);
    }

    [Fact]
    public void Views_DoNotHardcodeColors()
    {
        var files = Directory.EnumerateFiles(AppProjectDirectory(), "*.axaml", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Styles{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            Assert.False(HexColor().IsMatch(File.ReadAllText(file)), $"{Path.GetFileName(file)} містить жорстко заданий колір; використовуйте Lm.Brush.*");
        }
    }

    private static (string[] Dark, string[] Light) ReadVariants(string file)
    {
        var root = XDocument.Load(Path.Combine(AppProjectDirectory(), "Styles", file)).Root!;
        string[] Keys(string variant) => root.Descendants()
            .Where(element => (string?)element.Attribute(Xaml + "Key") == variant)
            .SelectMany(element => element.Elements())
            .Select(element => (string)element.Attribute(Xaml + "Key")!)
            .Order(StringComparer.Ordinal)
            .ToArray();

        return (Keys("Dark"), Keys("Light"));
    }

    private static string AppProjectDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "LocalMind.App");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("Не знайдено src/LocalMind.App.");
    }

    [GeneratedRegex("#[0-9A-Fa-f]{3,8}\\b")]
    private static partial Regex HexColor();
}
