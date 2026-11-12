using System.Text;
using LocalMind.Core.Configuration;
using LocalMind.Search.Embeddings;
using Microsoft.Extensions.Options;

namespace LocalMind.Search.Configuration;

/// <summary>
/// Перехресна перевірка розміру фрагмента з лімітом моделі. Розмір фрагмента рахується так, як його побачить модель:
/// <see cref="HfTokenCounter"/> враховує префікс <see cref="EmbeddingModelOptions.PassagePrefix"/>
/// і службові токени. Тому:
/// <list type="bullet">
/// <item>фрагмент не довший за <see cref="EmbeddingModelOptions.MaxTokens"/>, інакше модель мовчки обріже його;</item>
/// <item>фрагмент більший за резерв під префікс і службові токени, інакше на текст не лишиться місця.</item>
/// </list>
/// Якщо налаштування моделі самі некоректні, перевірка пропускається — про це вже повідомляє
/// <see cref="EmbeddingModelOptionsValidator"/>, і помилки не дублюються.
/// </summary>
public sealed class ChunkSizeWithinModelLimitValidator(IOptions<EmbeddingModelOptions> embeddingOptions)
    : IValidateOptions<ChunkingOptions>
{
    public ValidateOptionsResult Validate(string? name, ChunkingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        EmbeddingModelOptions model;
        try
        {
            model = embeddingOptions.Value;
        }
        catch (OptionsValidationException)
        {
            return ValidateOptionsResult.Skip;
        }

        if (options.ChunkSizeTokens > model.MaxTokens)
        {
            return ValidateOptionsResult.Fail(
                $"{ChunkingOptions.SectionName}:ChunkSizeTokens ({options.ChunkSizeTokens}) перевищує " +
                $"{EmbeddingModelOptions.SectionName}:MaxTokens ({model.MaxTokens}) — модель обрізатиме фрагменти. " +
                $"Зменште {ChunkingOptions.SectionName}:ChunkSizeTokens до ~{model.MaxTokens - (model.MaxTokens / 16)} " +
                "(змінна оточення LocalMind__Chunking__ChunkSizeTokens) або оберіть модель з більшим лімітом.");
        }

        var reserve = ReservedTokens(model);
        return options.ChunkSizeTokens <= reserve
            ? ValidateOptionsResult.Fail(
                $"{ChunkingOptions.SectionName}:ChunkSizeTokens ({options.ChunkSizeTokens}) не більший за резерв " +
                $"під префікс «{model.PassagePrefix}» і службові токени ({reserve}) — на текст не лишається місця.")
            : ValidateOptionsResult.Success;
    }

    /// <summary>
    /// Верхня оцінка токенів, які займають префікс і службові токени. Токенізатор тут не завантажуємо
    /// (застосунок стартує і без моделі). Кожен токен покриває щонайменше один символ, тож токенів не більше,
    /// ніж байтів UTF-8.
    /// </summary>
    public static int ReservedTokens(EmbeddingModelOptions model)
    {
        ArgumentNullException.ThrowIfNull(model);

        return ModelTokenizer.SpecialTokenCount + Encoding.UTF8.GetByteCount(model.PassagePrefix ?? string.Empty);
    }
}
