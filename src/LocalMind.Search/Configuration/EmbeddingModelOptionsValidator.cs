using Microsoft.Extensions.Options;

namespace LocalMind.Search.Configuration;

/// <summary>
/// Перевіряє <see cref="EmbeddingModelOptions"/> під час запуску. Існування файлів моделі тут не перевіряється:
/// без моделі застосунок має запуститися і працювати в режимі keyword-пошуку.
/// </summary>
public sealed class EmbeddingModelOptionsValidator : IValidateOptions<EmbeddingModelOptions>
{
    public ValidateOptionsResult Validate(string? name, EmbeddingModelOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var errors = new List<string>();
        const string Section = EmbeddingModelOptions.SectionName;

        if (string.IsNullOrWhiteSpace(options.ModelName))
        {
            errors.Add($"{Section}:ModelName не вказано.");
        }

        if (string.IsNullOrWhiteSpace(options.ModelPath))
        {
            errors.Add($"{Section}:ModelPath не вказано.");
        }

        if (string.IsNullOrWhiteSpace(options.TokenizerPath))
        {
            errors.Add($"{Section}:TokenizerPath не вказано.");
        }

        CheckHash(options.ModelSha256, nameof(EmbeddingModelOptions.ModelSha256), errors);
        CheckHash(options.TokenizerSha256, nameof(EmbeddingModelOptions.TokenizerSha256), errors);

        if (options.Dimensions <= 0)
        {
            errors.Add($"{Section}:Dimensions має бути більше 0 (зараз {options.Dimensions}).");
        }

        if (options.MaxTokens <= 0)
        {
            errors.Add($"{Section}:MaxTokens має бути більше 0 (зараз {options.MaxTokens}).");
        }

        if (options.BatchSize is <= 0 or > EmbeddingModelOptions.MaxBatchSize)
        {
            errors.Add(
                $"{Section}:BatchSize має бути від 1 до {EmbeddingModelOptions.MaxBatchSize} " +
                $"(зараз {options.BatchSize}).");
        }

        if (options.QueryPrefix is null || options.PassagePrefix is null)
        {
            errors.Add($"{Section}:QueryPrefix і PassagePrefix не можуть бути null (порожній рядок — без префікса).");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    private static void CheckHash(string? hash, string name, List<string> errors)
    {
        if (!string.IsNullOrEmpty(hash) && !(hash.Length == 64 && hash.All(char.IsAsciiHexDigit)))
        {
            errors.Add($"{EmbeddingModelOptions.SectionName}:{name} має бути SHA-256 з 64 hex-символів або порожнім.");
        }
    }
}
