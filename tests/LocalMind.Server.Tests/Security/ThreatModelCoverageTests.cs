using System.Reflection;
using System.Text.RegularExpressions;

namespace LocalMind.Server.Tests.Security;

/// <summary>
/// Звіряє <c>docs/threat-model.md</c> з тестами сервера (SV-10): кожна серверна загроза має наявні тести або
/// залишковий ризик, а теги <c>Threat</c> у тестах вказують на загрози, що справді є в документі.
/// Захищає документ від «гниття»: перейменований чи видалений тест одразу видно.
/// </summary>
public static partial class ThreatModelCoverageTests
{
    private static readonly Lazy<string> Document = new(() =>
        File.ReadAllText(Path.Combine(FindRepositoryRoot(), "docs", "threat-model.md")));

    [Fact]
    public static void EveryServerThreat_NamesExistingTestsOrIsResidualRisk()
    {
        var rows = ServerThreatRows().ToList();
        var methods = AllMethodNames();
        var problems = new List<string>();

        Assert.True(rows.Count >= 15, $"У розділі 4 знайдено лише {rows.Count} загроз — формат таблиці змінився?");
        foreach (var (id, text) in rows)
        {
            var tests = TestNameRegex().Matches(text).Select(m => m.Groups[1].Value).ToList();
            var isResidual = ResidualRegex().IsMatch(text);
            if (tests.Count == 0 && !isResidual)
            {
                problems.Add($"{id}: немає ні назви тесту, ні залишкового ризику (R-NN).");
            }

            problems.AddRange(tests
                .Where(name => !Exists(name, methods))
                .Select(name => $"{id}: тесту {name} немає в LocalMind.Server.Tests."));
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public static void EveryThreatTrait_PointsToThreatFromDocument()
    {
        var known = IdRegex().Matches(Document.Value).Select(m => m.Value).ToHashSet(StringComparer.Ordinal);
        var tagged = typeof(ThreatModelCoverageTests).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            .SelectMany(m => m.CustomAttributes
                .Where(a => a.AttributeType == typeof(TraitAttribute)
                    && a.ConstructorArguments is [{ Value: "Threat" }, { Value: string }])
                .Select(a => (Method: $"{m.DeclaringType!.Name}.{m.Name}", Id: (string)a.ConstructorArguments[1].Value!)))
            .ToList();

        Assert.NotEmpty(tagged);
        var unknown = tagged.Where(t => !known.Contains(t.Id)).Select(t => $"{t.Method}: {t.Id}").ToList();
        Assert.True(unknown.Count == 0, "Тест посилається на загрозу, якої немає в документі: " + string.Join("; ", unknown));
    }

    [Fact]
    public static void ServerThreats_ManyAreCoveredByTaggedScenarios()
    {
        // Кожен рядок, що не є залишковим ризиком, має щонайменше один тест: названий у документі (перевіряє
        // перший тест) або з тегом Threat. Тут лише рахуємо, скільки загроз покриті саме тегованими сценаріями.
        var tagged = typeof(ThreatModelCoverageTests).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            .SelectMany(m => m.CustomAttributes)
            .Where(a => a.AttributeType == typeof(TraitAttribute) && a.ConstructorArguments is [{ Value: "Threat" }, { Value: string }])
            .Select(a => (string)a.ConstructorArguments[1].Value!)
            .ToHashSet(StringComparer.Ordinal);

        var serverIds = ServerThreatRows().Select(r => r.Id).ToList();

        Assert.True(tagged.Count(serverIds.Contains) >= 10, "Тегованими сценаріями SV-10 покрито менше 10 серверних загроз.");
    }

    /// <summary>Корінь репозиторію: тека з <c>LocalMind.slnx</c>.</summary>
    internal static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LocalMind.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Не знайдено корінь репозиторію (LocalMind.slnx).");
    }

    /// <summary>Рядки таблиць розділу 4 («Сервер бекапу»): ID і весь текст рядка.</summary>
    private static IEnumerable<(string Id, string Text)> ServerThreatRows()
    {
        var start = Document.Value.IndexOf("## 4.", StringComparison.Ordinal);
        var end = Document.Value.IndexOf("## 5.", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "У документі немає розділів 4 і 5.");

        return Document.Value[start..end]
            .Split('\n')
            .Select(line => RowRegex().Match(line))
            .Where(m => m.Success)
            .Select(m => (m.Groups[1].Value, m.Value));
    }

    private static HashSet<string> AllMethodNames() =>
        typeof(ThreatModelCoverageTests).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Select(m => $"{t.Name}.{m.Name}")
                .Concat(t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static).Select(m => m.Name)))
            .ToHashSet(StringComparer.Ordinal);

    private static bool Exists(string name, HashSet<string> methods) => methods.Contains(name);

    [GeneratedRegex(@"^\|\s*(S-\d{2})\s*\|.*$")]
    private static partial Regex RowRegex();

    // Назви тестів у документі: `Клас.Метод_Умова_Результат` або `Метод_Умова_Результат` (обов'язково з підкресленням).
    [GeneratedRegex(@"`((?:[A-Za-z0-9]+\.)?[A-Za-z0-9]+_[A-Za-z0-9_]+)`")]
    private static partial Regex TestNameRegex();

    [GeneratedRegex(@"R-\d{2}")]
    private static partial Regex ResidualRegex();

    [GeneratedRegex(@"\b[SCB]-\d{2}\b|\bR-\d{2}\b")]
    private static partial Regex IdRegex();
}
