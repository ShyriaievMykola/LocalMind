namespace LocalMind.Search.Configuration;

/// <summary>Налаштування embedding-моделі. Секція конфігурації — <see cref="SectionName"/>.</summary>
public sealed class EmbeddingModelOptions
{
    /// <summary>Секція конфігурації.</summary>
    public const string SectionName = "LocalMind:Embedding";

    /// <summary>Найбільший допустимий розмір батчу.</summary>
    public const int MaxBatchSize = 1024;

    /// <summary>Унікальна назва моделі (наприклад, <c>multilingual-e5-small</c>).</summary>
    public string ModelName { get; set; } = string.Empty;

    /// <summary>Шлях до файла моделі ONNX.</summary>
    public string ModelPath { get; set; } = string.Empty;

    /// <summary>Шлях до файла токенізатора.</summary>
    public string TokenizerPath { get; set; } = string.Empty;

    /// <summary>Розмірність векторів моделі.</summary>
    public int Dimensions { get; set; }

    /// <summary>Максимальна кількість токенів одного тексту; довші тексти модель обрізає.</summary>
    public int MaxTokens { get; set; } = 512;

    /// <summary>Скільки текстів обробляти за один прохід моделі.</summary>
    public int BatchSize { get; set; } = 32;

    /// <summary>Префікс тексту запиту (для e5-моделей — <c>"query: "</c>), інакше порожній.</summary>
    public string QueryPrefix { get; set; } = string.Empty;

    /// <summary>Префікс фрагмента документа (для e5-моделей — <c>"passage: "</c>), інакше порожній.</summary>
    public string PassagePrefix { get; set; } = string.Empty;
}
