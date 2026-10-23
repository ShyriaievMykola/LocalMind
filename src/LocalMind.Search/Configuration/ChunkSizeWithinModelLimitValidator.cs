using LocalMind.Core.Configuration;
using Microsoft.Extensions.Options;

namespace LocalMind.Search.Configuration;

/// <summary>
/// Перехресна перевірка: фрагмент не може бути довшим за ліміт моделі, інакше модель
/// мовчки обріже його і частина тексту не потрапить у вектор.
/// Якщо налаштування моделі самі некоректні, перевірка пропускається — про це вже повідомляє
/// <see cref="EmbeddingModelOptionsValidator"/>, і помилки не дублюються.
/// </summary>
public sealed class ChunkSizeWithinModelLimitValidator(IOptions<EmbeddingModelOptions> embeddingOptions)
    : IValidateOptions<ChunkingOptions>
{
    public ValidateOptionsResult Validate(string? name, ChunkingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        int maxTokens;
        try
        {
            maxTokens = embeddingOptions.Value.MaxTokens;
        }
        catch (OptionsValidationException)
        {
            return ValidateOptionsResult.Skip;
        }

        return options.ChunkSizeTokens > maxTokens
            ? ValidateOptionsResult.Fail(
                $"{ChunkingOptions.SectionName}:ChunkSizeTokens ({options.ChunkSizeTokens}) перевищує " +
                $"{EmbeddingModelOptions.SectionName}:MaxTokens ({maxTokens}) — модель обрізатиме фрагменти.")
            : ValidateOptionsResult.Success;
    }
}
