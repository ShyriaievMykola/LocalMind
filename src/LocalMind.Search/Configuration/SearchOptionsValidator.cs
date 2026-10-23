using Microsoft.Extensions.Options;

namespace LocalMind.Search.Configuration;

/// <summary>Перевіряє <see cref="SearchOptions"/> під час запуску.</summary>
public sealed class SearchOptionsValidator : IValidateOptions<SearchOptions>
{
    public ValidateOptionsResult Validate(string? name, SearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var errors = new List<string>();
        const string Section = SearchOptions.SectionName;

        if (options.DefaultTopK <= 0)
        {
            errors.Add($"{Section}:DefaultTopK має бути більше 0 (зараз {options.DefaultTopK}).");
        }

        if (options.CandidatesPerIndex < options.DefaultTopK)
        {
            errors.Add(
                $"{Section}:CandidatesPerIndex ({options.CandidatesPerIndex}) має бути не менше " +
                $"за DefaultTopK ({options.DefaultTopK}).");
        }

        if (options.RrfK <= 0)
        {
            errors.Add($"{Section}:RrfK має бути більше 0 (зараз {options.RrfK}).");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
