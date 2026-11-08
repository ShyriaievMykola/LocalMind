using LocalMind.Core.Abstractions;
using LocalMind.Search.Configuration;
using Microsoft.Extensions.Options;

namespace LocalMind.Search.Embeddings;

/// <summary>
/// Рахує токени так, як їх побачить модель у фрагменті документа: разом із префіксом і службовими токенами.
/// Тому фрагмент у межах <see cref="EmbeddingModelOptions.MaxTokens"/> не обрізається під час обчислення вектора.
/// </summary>
public sealed class HfTokenCounter(ModelTokenizer tokenizer, IOptions<EmbeddingModelOptions> options) : ITokenCounter
{
    /// <inheritdoc />
    public int CountTokens(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return text.Length == 0 ? 0 : tokenizer.CountTokens(options.Value.PassagePrefix + text);
    }
}
