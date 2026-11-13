using LocalMind.Core.Abstractions;
using LocalMind.Search.Configuration;
using Microsoft.Extensions.Options;

namespace LocalMind.Search.Embeddings;

/// <summary>
/// Рахує токени так, як їх побачить модель у фрагменті документа: разом із префіксом і службовими токенами.
/// Тому фрагмент у межах <see cref="EmbeddingModelOptions.MaxTokens"/> не обрізається під час обчислення вектора.
/// Якщо токенізатора немає (модель не завантажено), токени оцінюються за довжиною тексту: фрагменти потрібні
/// лише для keyword-пошуку, і точні межі для них не важливі.
/// </summary>
public sealed class HfTokenCounter(ModelTokenizer tokenizer, IOptions<EmbeddingModelOptions> options) : ITokenCounter
{
    /// <summary>Приблизно один токен SentencePiece на три символи (для української й англійської — трохи з запасом).</summary>
    internal const int CharsPerEstimatedToken = 3;

    /// <inheritdoc />
    public int CountTokens(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length == 0)
        {
            return 0;
        }

        var full = options.Value.PassagePrefix + text;
        return tokenizer.IsAvailable ? tokenizer.CountTokens(full) : Estimate(full);
    }

    private static int Estimate(string text) =>
        ModelTokenizer.SpecialTokenCount + ((text.Length + CharsPerEstimatedToken - 1) / CharsPerEstimatedToken);
}
