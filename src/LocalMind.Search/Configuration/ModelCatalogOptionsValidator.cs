using Microsoft.Extensions.Options;

namespace LocalMind.Search.Configuration;

/// <summary>Перевіряє <see cref="ModelCatalogOptions"/>: у каталозі кожна модель має повний, перевірний опис.</summary>
public sealed class ModelCatalogOptionsValidator : IValidateOptions<ModelCatalogOptions>
{
    // Заборонені на будь-якій ОС (набір Windows ширший за Linux), щоб каталог працював однаково всюди.
    private static readonly System.Buffers.SearchValues<char> ForbiddenChars = System.Buffers.SearchValues.Create(['/', '\\', ':', '*', '?', '"', '<', '>', '|']);

    public ValidateOptionsResult Validate(string? name, ModelCatalogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var errors = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < options.Models.Count; i++)
        {
            var model = options.Models[i];
            var prefix = $"{ModelCatalogOptions.SectionName}:Models:{i}";

            if (!IsPlainFileName(model.Name))
            {
                errors.Add($"{prefix}:Name має бути назвою теки без розділювачів шляху (зараз «{model.Name}»).");
            }
            else if (!seen.Add(model.Name))
            {
                errors.Add($"{prefix}:Name «{model.Name}» повторюється.");
            }

            Require(model.Repository, $"{prefix}:Repository", errors);
            Require(model.License, $"{prefix}:License", errors);
            RequireRelativePath(model.ModelFile, $"{prefix}:ModelFile", errors);
            RequireRelativePath(model.TokenizerFile, $"{prefix}:TokenizerFile", errors);
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

    /// <summary>Назва без шляху: без недозволених в імені файла символів, розділювачів і «..».</summary>
    private static bool IsPlainFileName(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value is not ("." or "..")
        && value.IndexOfAny(ForbiddenChars) < 0
        && !value.Any(char.IsControl);

    /// <summary>Шлях усередині репозиторію Hugging Face: відносний, без «..» і без недозволених символів.</summary>
    private static void RequireRelativePath(string? value, string key, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{key} не вказано.");
            return;
        }

        var valid = !Path.IsPathRooted(value) && value.Split('/').All(IsPlainFileName);
        if (!valid)
        {
            errors.Add($"{key} має бути відносним шляхом усередині репозиторію без «..» і недозволених символів (зараз «{value}»).");
        }
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
