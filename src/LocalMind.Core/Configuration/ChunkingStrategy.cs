namespace LocalMind.Core.Configuration;

/// <summary>Стратегія нарізки тексту на фрагменти (експеримент E3).</summary>
public enum ChunkingStrategy
{
    /// <summary>Фрагменти фіксованого розміру в токенах з перекриттям.</summary>
    FixedSize,

    /// <summary>Фрагменти по абзацах і заголовках; довгі абзаци діляться.</summary>
    Paragraph,
}
