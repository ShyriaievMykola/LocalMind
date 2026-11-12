using Microsoft.Extensions.Options;

namespace LocalMind.Search.Configuration;

/// <summary>Перевіряє <see cref="ModelCatalogOptions"/>: у каталозі кожна модель має повний, перевірний опис.</summary>
public sealed class ModelCatalogOptionsValidator : IValidateOptions<ModelCatalogOptions>
{
    public ValidateOptionsResult Validate(string? name, ModelCatalogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var errors = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < options.Models.Count; i++)
        {
            var model = options.Models[i];
            var prefix = $"{ModelCatalogOptions.SectionName}:Models:{i}";

            if (string.IsNullOrWhiteSpace(model.Name) || model.Name.IndexOfAny(['/', '\\', ':']) >= 0 || model.Name.Contains("..", StringComparison.Ordinal))
            {
                errors.Add($"{prefix}:Name має бути назвою теки без розділювачів шляху (зараз «{model.Name}»).");
            }
            else if (!seen.Add(model.Name))
            {
                errors.Add($"{prefix}:Name «{model.Name}» повторюється.");
            }

            Require(model.Repository, $"{prefix}:Repository", errors);
            Require(model.License, $"{prefix}:License", errors);
            Require(model.ModelFile, $"{prefix}:ModelFile", errors);
            Require(model.TokenizerFile, $"{prefix}:TokenizerFile", errors);
            RequireHex(model.Revision, 40, $"{prefix}:Revision (повний коміт)", errors);
            RequireHex(model.ModelSha256, 64, $"{prefix}:ModelSha256", errors);
            RequireHex(model.TokenizerSha256, 64, $"{prefix}:TokenizerSha256", errors);

            if (model.Dimensions <= 0)
            {
                errors.Add($"{prefix}:Dimensions має бути більше 0 (зараз {model.Dimensions}).");
            }

            if (model.MaxTokens <= 0)
            {
                errors.Add($"{prefix}:MaxTokens має бути більше 0 (зараз {model.MaxTokens}).");
            }

            if (model.ModelBytes <= 0)
            {
                errors.Add($"{prefix}:ModelBytes має бути більше 0.");
            }

            if (model.QueryPrefix is null || model.PassagePrefix is null)
            {
                errors.Add($"{prefix}:QueryPrefix і PassagePrefix не можуть бути null.");
            }
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    private static void Require(string? value, string key, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{key} не вказано.");
        }
    }

    private static void RequireHex(string? value, int length, string key, List<string> errors)
    {
        if (value is null || value.Length != length || !value.All(char.IsAsciiHexDigit))
        {
            errors.Add($"{key} має бути {length} hex-символів.");
        }
    }
}
