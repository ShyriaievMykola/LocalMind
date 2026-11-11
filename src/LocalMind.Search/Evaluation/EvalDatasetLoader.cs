using System.Text.Json;
using System.Text.Json.Serialization;
using LocalMind.Core.Errors;

namespace LocalMind.Search.Evaluation;

/// <summary>
/// Читає й перевіряє датасет: тека з <c>queries.jsonl</c>, <c>qrels.jsonl</c> і підтекою <c>corpus/</c>.
/// Будь-яка вада розмітки — <see cref="LocalMindException"/> з файлом і номером рядка, щоб її можна було виправити,
/// а не гадати, чому метрики дивні.
/// </summary>
public static class EvalDatasetLoader
{
    /// <summary>Назва файла із запитами.</summary>
    public const string QueriesFile = "queries.jsonl";

    /// <summary>Назва файла з розміткою релевантності.</summary>
    public const string QrelsFile = "qrels.jsonl";

    /// <summary>Назва теки з корпусом.</summary>
    public const string CorpusFolder = "corpus";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    /// <summary>Завантажує датасет з теки.</summary>
    /// <exception cref="LocalMindException">Файла чи теки немає або розмітка некоректна.</exception>
    public static EvalDataset Load(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var corpus = Path.Combine(directory, CorpusFolder);
        if (!Directory.Exists(corpus))
        {
            throw new LocalMindException($"Датасет {directory}: немає теки {CorpusFolder}/ з документами.");
        }

        var queries = ReadQueries(Path.Combine(directory, QueriesFile));
        var corpusFiles = Directory.EnumerateFiles(corpus, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(corpus, file).Replace('\\', '/'))
            .ToHashSet(StringComparer.Ordinal);
        var relevance = ReadQrels(Path.Combine(directory, QrelsFile), queries, corpusFiles);

        return new EvalDataset(corpus, queries, relevance);
    }

    private static List<EvalQuery> ReadQueries(string path)
    {
        var queries = new List<EvalQuery>();
        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (line, number) in ReadLines(path))
        {
            var row = Parse<QueryRow>(line, path, number);
            if (string.IsNullOrWhiteSpace(row.Id))
            {
                throw Error(path, number, "порожній id запиту.");
            }

            if (string.IsNullOrWhiteSpace(row.Text))
            {
                throw Error(path, number, $"запит {row.Id} без тексту.");
            }

            if (row.Type is null)
            {
                throw Error(path, number, $"запит {row.Id}: не вказано type (exact, paraphrase, crossLingual).");
            }

            if (!ids.Add(row.Id))
            {
                throw Error(path, number, $"дубльований id запиту {row.Id}.");
            }

            queries.Add(new EvalQuery(row.Id, row.Text.Trim(), row.Type.Value));
        }

        if (queries.Count == 0)
        {
            throw new LocalMindException($"{path}: немає жодного запиту.");
        }

        return queries;
    }

    private static Dictionary<string, IReadOnlyDictionary<string, int>> ReadQrels(
        string path,
        List<EvalQuery> queries,
        HashSet<string> corpusFiles)
    {
        var known = queries.Select(q => q.Id).ToHashSet(StringComparer.Ordinal);
        var relevance = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);

        foreach (var (line, number) in ReadLines(path))
        {
            var row = Parse<QrelRow>(line, path, number);
            if (string.IsNullOrWhiteSpace(row.Query) || !known.Contains(row.Query))
            {
                throw Error(path, number, $"невідомий запит {row.Query ?? "(порожньо)"}: його немає в {QueriesFile}.");
            }

            if (string.IsNullOrWhiteSpace(row.Document))
            {
                throw Error(path, number, $"запит {row.Query}: порожній document.");
            }

            var document = row.Document.Replace('\\', '/');
            if (!corpusFiles.Contains(document))
            {
                throw Error(path, number, $"запит {row.Query}: документа {document} немає в {CorpusFolder}/.");
            }

            if (row.Relevance is not (>= 1 and <= 3))
            {
                throw Error(path, number, $"запит {row.Query}, {document}: relevance має бути 1, 2 або 3.");
            }

            if (!relevance.TryGetValue(row.Query, out var documents))
            {
                documents = new Dictionary<string, int>(StringComparer.Ordinal);
                relevance[row.Query] = documents;
            }

            if (!documents.TryAdd(document, row.Relevance.Value))
            {
                throw Error(path, number, $"запит {row.Query}: документ {document} уже розмічено.");
            }
        }

        var unmarked = queries.FirstOrDefault(q => !relevance.ContainsKey(q.Id));
        if (unmarked is not null)
        {
            throw new LocalMindException($"{path}: для запиту {unmarked.Id} немає жодного релевантного документа.");
        }

        return relevance.ToDictionary(p => p.Key, p => (IReadOnlyDictionary<string, int>)p.Value, StringComparer.Ordinal);
    }

    private static IEnumerable<(string Line, int Number)> ReadLines(string path)
    {
        if (!File.Exists(path))
        {
            throw new LocalMindException($"Файл не знайдено: {path}.");
        }

        var number = 0;
        foreach (var line in File.ReadLines(path))
        {
            number++;
            if (!string.IsNullOrWhiteSpace(line))
            {
                yield return (line, number);
            }
        }
    }

    private static T Parse<T>(string line, string path, int number)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(line, JsonOptions) ?? throw Error(path, number, "порожній об'єкт.");
        }
        catch (JsonException ex)
        {
            throw new LocalMindException($"{path}, рядок {number}: некоректний JSON ({ex.Message}).", ex);
        }
    }

    private static LocalMindException Error(string path, int number, string message) =>
        new($"{path}, рядок {number}: {message}");

    private sealed record QueryRow(string? Id, string? Text, QueryType? Type);

    private sealed record QrelRow(string? Query, string? Document, int? Relevance);
}
