using Microsoft.Extensions.Options;

namespace LocalMind.Search.Configuration;

/// <summary>Перевіряє <see cref="VectorIndexOptions"/> під час запуску.</summary>
public sealed class VectorIndexOptionsValidator : IValidateOptions<VectorIndexOptions>
{
    /// <summary>Мінімальне M: при меншому граф не зв'язується.</summary>
    public const int MinM = 2;

    /// <summary>Верхня межа M, за якою додавання стає непрактично повільним.</summary>
    public const int MaxM = 128;

    public ValidateOptionsResult Validate(string? name, VectorIndexOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var errors = new List<string>();
        const string Section = VectorIndexOptions.SectionName;

        if (!Enum.IsDefined(options.Kind))
        {
            errors.Add($"{Section}:Kind — невідомий індекс «{options.Kind}».");
        }

        if (options.M is < MinM or > MaxM)
        {
            errors.Add($"{Section}:M має бути від {MinM} до {MaxM} (зараз {options.M}).");
        }

        if (options.EfConstruction < options.M)
        {
            errors.Add($"{Section}:EfConstruction ({options.EfConstruction}) має бути не менше за M ({options.M}).");
        }

        if (options.EfSearch <= 0)
        {
            errors.Add($"{Section}:EfSearch має бути більше 0 (зараз {options.EfSearch}).");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
