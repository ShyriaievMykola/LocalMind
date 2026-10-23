using Microsoft.Extensions.Options;

namespace LocalMind.Core.Configuration;

/// <summary>Перевіряє <see cref="ChunkingOptions"/> під час запуску.</summary>
public sealed class ChunkingOptionsValidator : IValidateOptions<ChunkingOptions>
{
    public ValidateOptionsResult Validate(string? name, ChunkingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var errors = new List<string>();
        const string Section = ChunkingOptions.SectionName;

        if (!Enum.IsDefined(options.Strategy))
        {
            errors.Add($"{Section}:Strategy — невідома стратегія «{options.Strategy}».");
        }

        if (options.ChunkSizeTokens <= 0)
        {
            errors.Add($"{Section}:ChunkSizeTokens має бути більше 0 (зараз {options.ChunkSizeTokens}).");
        }

        if (options.OverlapPercent is < 0 or > ChunkingOptions.MaxOverlapPercent)
        {
            errors.Add(
                $"{Section}:OverlapPercent має бути від 0 до {ChunkingOptions.MaxOverlapPercent} " +
                $"(зараз {options.OverlapPercent}).");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
