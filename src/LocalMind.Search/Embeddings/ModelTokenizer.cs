using LocalMind.Core.Errors;
using LocalMind.Search.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.ML.Tokenizers;

namespace LocalMind.Search.Embeddings;

/// <summary>
/// Токенізатор XLM-Roberta (моделі сімейства e5): SentencePiece з номерами токенів, зсунутими на 1,
/// бо перші номери зайняті службовими токенами. Файл завантажується під час першого використання.
/// </summary>
public sealed class ModelTokenizer
{
    /// <summary>Номер токена доповнення.</summary>
    public const long PadId = 1;

    private const long BeginId = 0;
    private const long EndId = 2;
    private const long UnknownId = 3;
    private const int SentencePieceUnknownId = 0;
    private const int IdOffset = 1;
    /// <summary>Службові токени початку й кінця тексту.</summary>
    internal const int SpecialTokenCount = 2;

    private readonly EmbeddingModelOptions _options;
    private readonly LoadOnce<SentencePieceTokenizer> _tokenizer;

    public ModelTokenizer(IOptions<EmbeddingModelOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
        _tokenizer = new LoadOnce<SentencePieceTokenizer>(Load);
    }

    /// <summary>Кількість токенів разом зі службовими; 0 для порожнього тексту.</summary>
    public int CountTokens(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return text.Length == 0 ? 0 : EncodePieces(text).Count + SpecialTokenCount;
    }

    /// <summary>Номери токенів для моделі; довгий текст обрізається до <paramref name="maxTokens"/> токенів.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="maxTokens"/> не вміщує службові токени і хоча б один токен тексту.
    /// </exception>
    public long[] Encode(string text, int maxTokens)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxTokens, SpecialTokenCount + 1);

        var pieces = EncodePieces(text);
        var pieceCount = Math.Min(pieces.Count, maxTokens - SpecialTokenCount);

        var ids = new long[pieceCount + SpecialTokenCount];
        ids[0] = BeginId;
        for (var i = 0; i < pieceCount; i++)
        {
            ids[i + 1] = pieces[i] == SentencePieceUnknownId ? UnknownId : pieces[i] + IdOffset;
        }

        ids[^1] = EndId;
        return ids;
    }

    private IReadOnlyList<int> EncodePieces(string text) =>
        _tokenizer.Value.EncodeToIds(text, addBeginningOfSentence: false, addEndOfSentence: false);

    private SentencePieceTokenizer Load()
    {
        if (!File.Exists(_options.TokenizerPath))
        {
            throw new LocalMindException(
                $"Файл токенізатора не знайдено: {_options.TokenizerPath}. Запустіть scripts/download-models.");
        }

        try
        {
            using var stream = File.OpenRead(_options.TokenizerPath);
            return SentencePieceTokenizer.Create(stream, false, false);
        }
#pragma warning disable CA1031 // Бібліотека кидає різні типи на пошкоджений файл; усі вони для нас означають одне.
        catch (Exception ex) when (ex is not OutOfMemoryException)
#pragma warning restore CA1031
        {
            throw new LocalMindException(
                $"Файл токенізатора пошкоджений: {_options.TokenizerPath}. Запустіть scripts/download-models ще раз.", ex);
        }
    }
}
