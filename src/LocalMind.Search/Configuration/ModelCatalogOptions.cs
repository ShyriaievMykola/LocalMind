namespace LocalMind.Search.Configuration;

/// <summary>
/// Каталог embedding-моделей, які підтримує LocalMind (експеримент E2). Секція конфігурації — <see cref="SectionName"/>.
/// Модель обирається назвою: якщо <c>LocalMind:Embedding:ModelName</c> є в каталозі, значення звідси
/// <b>перезаписують</b> розмірність, ліміт токенів, префікси й хеші з конфігурації користувача;
/// шляхи беруться з каталогу лише якщо не задані.
/// </summary>
public sealed class ModelCatalogOptions
{
    /// <summary>Секція конфігурації.</summary>
    public const string SectionName = "LocalMind:ModelCatalog";

    /// <summary>Моделі каталогу.</summary>
    public List<CatalogModel> Models { get; set; } = [];

    /// <summary>Знаходить модель за назвою (без урахування регістру).</summary>
    public CatalogModel? Find(string? name) =>
        Models.Find(model => string.Equals(model.Name, name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Одна модель каталогу. Токенізатор має бути XLM-RoBERTa SentencePiece (<c>sentencepiece.bpe.model</c>),
/// а модель — повертати вектори на токен, які <c>OnnxEmbedder</c> усереднює (mean pooling).
/// </summary>
public sealed class CatalogModel
{
    /// <summary>Назва, вона ж тека в <c>models/</c>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Репозиторій на Hugging Face, наприклад <c>intfloat/multilingual-e5-small</c>.</summary>
    public string Repository { get; set; } = string.Empty;

    /// <summary>Повний коміт репозиторію (40 hex): завантажується саме ця версія.</summary>
    public string Revision { get; set; } = string.Empty;

    /// <summary>Ліцензія моделі.</summary>
    public string License { get; set; } = string.Empty;

    /// <summary>Шлях до файла ONNX у репозиторії.</summary>
    public string ModelFile { get; set; } = "onnx/model.onnx";

    /// <summary>Шлях до файла токенізатора в репозиторії.</summary>
    public string TokenizerFile { get; set; } = "sentencepiece.bpe.model";

    /// <summary>Розмір файла моделі в байтах (для попередження про обсяг завантаження).</summary>
    public long ModelBytes { get; set; }

    /// <summary>SHA-256 файла моделі.</summary>
    public string ModelSha256 { get; set; } = string.Empty;

    /// <summary>SHA-256 файла токенізатора.</summary>
    public string TokenizerSha256 { get; set; } = string.Empty;

    /// <summary>Розмірність векторів.</summary>
    public int Dimensions { get; set; }

    /// <summary>Скільки токенів модель обробляє, не гублячи якість (для sentence-transformers — довжина навчання).</summary>
    public int MaxTokens { get; set; } = 512;

    /// <summary>Префікс запиту (для e5 — <c>"query: "</c>).</summary>
    public string QueryPrefix { get; set; } = string.Empty;

    /// <summary>Префікс фрагмента документа (для e5 — <c>"passage: "</c>).</summary>
    public string PassagePrefix { get; set; } = string.Empty;

    /// <summary>Шлях до моделі відносно поточної папки: <c>models/&lt;Name&gt;/model.onnx</c>.</summary>
    public string LocalModelPath => $"models/{Name}/model.onnx";

    /// <summary>Шлях до токенізатора відносно поточної папки.</summary>
    public string LocalTokenizerPath => $"models/{Name}/sentencepiece.bpe.model";
}
