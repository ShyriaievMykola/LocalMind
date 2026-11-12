using Microsoft.Extensions.Options;

namespace LocalMind.Core.Configuration;

/// <summary>Перевіряє <see cref="IngestionOptions"/> під час запуску.</summary>
public sealed class IngestionOptionsValidator : IValidateOptions<IngestionOptions>
{
    public ValidateOptionsResult Validate(string? name, IngestionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var errors = new List<string>();
        const string Section = IngestionOptions.SectionName;

        if (options.MaxFileSizeBytes <= 0)
        {
            errors.Add($"{Section}:MaxFileSizeBytes має бути більше 0 (зараз {options.MaxFileSizeBytes}).");
        }

        if (options.MaxArchiveEntries <= 0)
        {
            errors.Add($"{Section}:MaxArchiveEntries має бути більше 0 (зараз {options.MaxArchiveEntries}).");
        }

        if (options.MaxArchiveUncompressedBytes <= 0)
        {
            errors.Add(
                $"{Section}:MaxArchiveUncompressedBytes має бути більше 0 (зараз {options.MaxArchiveUncompressedBytes}).");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
