namespace LocalMind.Search.Configuration;

/// <summary>Який індекс векторів використовувати.</summary>
public enum VectorIndexKind
{
    /// <summary>Точний повний перебір (за замовчуванням).</summary>
    BruteForce,

    /// <summary>Наближений граф HNSW.</summary>
    Hnsw,
}

/// <summary>Налаштування індексу векторів. Секція конфігурації — <see cref="SectionName"/>.</summary>
public sealed class VectorIndexOptions
{
    /// <summary>Секція конфігурації.</summary>
    public const string SectionName = "LocalMind:Search:VectorIndex";

    /// <summary>Обраний індекс. Brute-force точний і швидкий для тисяч векторів; HNSW — для великих бібліотек.</summary>
    public VectorIndexKind Kind { get; set; } = VectorIndexKind.BruteForce;

    /// <summary>HNSW, M: скільки зв'язків має вузол на верхніх шарах (на нижньому — 2·M). Більше — точніше й більше пам'яті.</summary>
    public int M { get; set; } = 16;

    /// <summary>HNSW, efConstruction: ширина пошуку кандидатів під час додавання. Більше — точніший граф, повільніше додавання.</summary>
    public int EfConstruction { get; set; } = 200;

    /// <summary>HNSW, efSearch: ширина пошуку під час запиту (не менша за k). Більше — точніше й повільніше.</summary>
    public int EfSearch { get; set; } = 64;
}
