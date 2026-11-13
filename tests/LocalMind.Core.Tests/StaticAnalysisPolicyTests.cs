using System.Text.RegularExpressions;

namespace LocalMind.Core.Tests;

/// <summary>
/// Захист критерію C4: приглушити попередження аналізатора можна лише з поясненням, у відомих місцях.
/// Перелік дозволених приглушень і причини — docs/static-analysis.md.
/// </summary>
public sealed partial class StaticAnalysisPolicyTests
{
    private static readonly string Root = FindRoot();

    [Fact]
    public void NoWarn_IsOnlyInDirectoryBuildPropsAndOnlyForMissingXmlDocs()
    {
        var found = ProjectFiles()
            .Where(file => File.ReadAllText(file).Contains("NoWarn", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(Root, file).Replace('\\', '/'))
            .ToList();

        Assert.Equal(["Directory.Build.props"], found);
        Assert.Contains("<NoWarn>$(NoWarn);CS1591</NoWarn>", File.ReadAllText(Path.Combine(Root, "Directory.Build.props")), StringComparison.Ordinal);
    }

    [Fact]
    public void PragmaWarningDisable_AlwaysHasAnExplanation()
    {
        var unexplained = SourceFiles()
            .SelectMany(file => File.ReadLines(file).Select((line, index) => (file, line, number: index + 1)))
            .Where(item => item.line.TrimStart().StartsWith("#pragma warning disable", StringComparison.Ordinal))
            .Where(item => !PragmaWithComment().IsMatch(item.line))
            .Select(item => $"{Path.GetRelativePath(Root, item.file)}:{item.number}")
            .ToList();

        Assert.True(unexplained.Count == 0, "#pragma warning disable без пояснення (// причина): " + string.Join(", ", unexplained));
    }

    [Fact]
    public void SuppressMessage_AlwaysHasAJustification()
    {
        var unexplained = SourceFiles()
            .Where(file => !file.EndsWith(nameof(StaticAnalysisPolicyTests) + ".cs", StringComparison.Ordinal))
            .Where(file => File.ReadAllText(file).Contains("SuppressMessage", StringComparison.Ordinal))
            .Where(file => !Regex.IsMatch(File.ReadAllText(file), @"SuppressMessage\([^)]*Justification\s*=\s*""[^""]{10,}"""))
            .Select(file => Path.GetRelativePath(Root, file))
            .ToList();

        Assert.True(unexplained.Count == 0, "SuppressMessage без Justification: " + string.Join(", ", unexplained));
        Assert.DoesNotContain(SourceFiles(), file => Path.GetFileName(file).Equals("GlobalSuppressions.cs", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EditorConfig_DisabledRules_AreExplainedByAPrecedingComment()
    {
        var lines = File.ReadAllLines(Path.Combine(Root, ".editorconfig"));
        var unexplained = new List<string>();
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("dotnet_diagnostic.", StringComparison.Ordinal)
                && lines[i].TrimEnd().EndsWith("= none", StringComparison.Ordinal)
                && !HasCommentAbove(lines, i))
            {
                unexplained.Add($".editorconfig:{i + 1} {lines[i]}");
            }
        }

        Assert.True(unexplained.Count == 0, "Вимкнені правила без коментаря: " + string.Join("; ", unexplained));
    }

    [Fact]
    public void SecurityAnalyzers_AreFullyEnabledAndWarningsBreakCiBuilds()
    {
        var props = File.ReadAllText(Path.Combine(Root, "Directory.Build.props"));

        Assert.Contains("<AnalysisLevelSecurity>latest-all</AnalysisLevelSecurity>", props, StringComparison.Ordinal);
        Assert.Contains("<TreatWarningsAsErrors Condition=\"'$(CI)' == 'true'\">true</TreatWarningsAsErrors>", props, StringComparison.Ordinal);
        Assert.Contains("<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>", props, StringComparison.Ordinal);
        Assert.Contains("<NuGetAuditMode>all</NuGetAuditMode>", props, StringComparison.Ordinal);
    }

    /// <summary>Коментар у попередніх рядках того самого блоку (до порожнього рядка).</summary>
    private static bool HasCommentAbove(string[] lines, int index)
    {
        for (var i = index - 1; i >= 0 && !string.IsNullOrWhiteSpace(lines[i]); i--)
        {
            if (lines[i].TrimStart().StartsWith('#'))
            {
                return true;
            }
        }

        return false;
    }

    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", ".idea", ".vscode", "bin", "obj", "node_modules", "Migrations",
    };

    private static IEnumerable<string> SourceFiles() => Walk(Root).Where(file => file.EndsWith(".cs", StringComparison.Ordinal));

    private static IEnumerable<string> ProjectFiles() =>
        Walk(Root).Where(file => file.EndsWith(".csproj", StringComparison.Ordinal)
            || file.EndsWith(".props", StringComparison.Ordinal)
            || file.EndsWith(".targets", StringComparison.Ordinal));

    /// <summary>
    /// Обхід, що не заходить у службові й згенеровані каталоги (.git, .vs, bin, obj, node_modules, міграції EF)
    /// та в сусідні worktree (wt-*), а не відфільтровує їх вже після повного перелічення.
    /// </summary>
    private static IEnumerable<string> Walk(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory))
        {
            yield return file;
        }

        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            var name = Path.GetFileName(child);
            if (SkippedDirectories.Contains(name) || name.StartsWith("wt-", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var file in Walk(child))
            {
                yield return file;
            }
        }
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LocalMind.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Корінь репозиторію (LocalMind.slnx) не знайдено над тестовою текою.");
    }

    [GeneratedRegex(@"#pragma warning disable\s+\S+.*//\s*\S.{9,}")]
    private static partial Regex PragmaWithComment();
}
