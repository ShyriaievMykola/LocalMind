using LocalMind.Core.Abstractions;

namespace LocalMind.Search.Embeddings;

/// <summary>Рахує токени токенізатором моделі, тому chunker і модель бачать однакову кількість.</summary>
public sealed class HfTokenCounter(ModelTokenizer tokenizer) : ITokenCounter
{
    /// <inheritdoc />
    public int CountTokens(string text) => tokenizer.CountTokens(text);
}
