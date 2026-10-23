namespace LocalMind.Core.Configuration;

/// <summary>Налаштування нарізки документів на фрагменти. Секція конфігурації — <see cref="SectionName"/>.</summary>
public sealed class ChunkingOptions
{
    /// <summary>Секція конфігурації.</summary>
    public const string SectionName = "LocalMind:Chunking";

    /// <summary>Найбільше допустиме перекриття фрагментів, %.</summary>
    public const int MaxOverlapPercent = 50;

    /// <summary>Стратегія нарізки.</summary>
    public ChunkingStrategy Strategy { get; set; } = ChunkingStrategy.FixedSize;

    /// <summary>Розмір фрагмента в токенах; не більше ліміту моделі.</summary>
    public int ChunkSizeTokens { get; set; } = 256;

    /// <summary>Перекриття сусідніх фрагментів, % від розміру фрагмента (0 — без перекриття).</summary>
    public int OverlapPercent { get; set; } = 15;
}
