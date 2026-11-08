using LocalMind.Search.Configuration;

namespace LocalMind.Search.Tests.Embeddings;

/// <summary>Тест зі справжньою моделлю; пропускається, якщо її не завантажено (наприклад, у CI).</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ModelFactAttribute : FactAttribute
{
    public ModelFactAttribute()
    {
        if (TestModel.Folder is null)
        {
            Skip = "Модель не завантажено: запустіть scripts/download-models.";
        }
    }
}

/// <summary>Налаштування завантаженої моделі <c>multilingual-e5-small</c>.</summary>
public static class TestModel
{
    private const string ModelName = "multilingual-e5-small";

    /// <summary>Тека моделі в корені репозиторію або <see langword="null"/>, якщо її немає.</summary>
    public static string? Folder { get; } = FindFolder();

    public static EmbeddingModelOptions Options(int batchSize = 32) => new()
    {
        ModelName = ModelName,
        ModelPath = Path.Combine(Folder ?? string.Empty, "model.onnx"),
        TokenizerPath = Path.Combine(Folder ?? string.Empty, "sentencepiece.bpe.model"),
        Dimensions = 384,
        BatchSize = batchSize,
        QueryPrefix = "query: ",
        PassagePrefix = "passage: ",
    };

    private static string? FindFolder()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "models", ModelName);
            if (File.Exists(Path.Combine(candidate, "model.onnx")))
            {
                return candidate;
            }
        }

        return null;
    }
}
