using System.Globalization;
using LocalMind.Core.Configuration;
using LocalMind.Core.Models;
using LocalMind.Search.Configuration;
using Microsoft.Extensions.Configuration;

namespace LocalMind.Search.Evaluation;

/// <summary>
/// Одна комбінація нарізки й моделі, для якої корпус індексується окремо, і прогони, що її діляться.
/// </summary>
/// <param name="Runs">Прогони з однаковими ефективними нарізкою й моделлю, у порядку конфігу.</param>
/// <param name="ConfigOverrides">
/// Налаштування, які треба накласти на конфігурацію застосунку, щоб отримати цю комбінацію
/// (ключі у форматі <c>LocalMind:Section:Name</c>).
/// </param>
/// <param name="Setup">Ефективні модель і нарізка (з урахуванням значень за замовчуванням і каталогу).</param>
/// <param name="ModelPath">Ефективний шлях до файла моделі.</param>
/// <param name="TokenizerPath">Ефективний шлях до файла токенізатора.</param>
/// <param name="Problems">Що не так з групою ще до запуску (немає моделі в каталозі, розмір фрагмента більший за ліміт моделі).</param>
public sealed record EvalGroup(
    IReadOnlyList<EvalRun> Runs,
    IReadOnlyDictionary<string, string?> ConfigOverrides,
    EvalSetup Setup,
    string ModelPath,
    string TokenizerPath,
    IReadOnlyList<string> Problems)
{
    /// <summary>Чи потрібна модель embeddings: є режим, відмінний від keyword.</summary>
    public bool RequiresModel => Runs.Any(run => run.Modes.Any(mode => mode != SearchMode.Keyword));

    /// <summary>Назви прогонів групи для повідомлень.</summary>
    public string RunNames => string.Join(", ", Runs.Select(run => run.Name));
}

/// <summary>
/// Розбиває прогони експерименту на групи за ефективними нарізкою й моделлю: кожна група — це окремий індекс у власній
/// тимчасовій базі, тож бібліотека користувача не змінюється, а результати різних комбінацій не змішуються.
/// Порівнюються ефективні значення, а не сирі поля конфігу: прогін без <c>chunking</c>, з порожнім <c>chunking</c>
/// чи з явно вказаними значеннями за замовчуванням — це та сама комбінація й один індекс.
/// </summary>
public static class EvalPlan
{
    /// <summary>Групує прогони; порядок груп і прогонів — як у конфігу.</summary>
    /// <param name="runs">Прогони експерименту.</param>
    /// <param name="configuration">Конфігурація застосунку: значення за замовчуванням і каталог моделей.</param>
    public static IReadOnlyList<EvalGroup> Build(IReadOnlyList<EvalRun> runs, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(configuration);

        var catalog = configuration.GetSection(ModelCatalogOptions.SectionName).Get<ModelCatalogOptions>() ?? new ModelCatalogOptions();
        var chunkingDefaults = configuration.GetSection(ChunkingOptions.SectionName).Get<ChunkingOptions>() ?? new ChunkingOptions();
        var embeddingDefaults = configuration.GetSection(EmbeddingModelOptions.SectionName).Get<EmbeddingModelOptions>()
            ?? new EmbeddingModelOptions();

        return [.. runs
            .Select(run => (Run: run, Effective: Resolve(run, chunkingDefaults, embeddingDefaults, catalog)))
            .GroupBy(item => (
                item.Effective.Setup.Model.ToLowerInvariant(),
                item.Effective.Setup.Strategy,
                item.Effective.Setup.ChunkSizeTokens,
                item.Effective.Setup.OverlapPercent))
            .Select(group => ToGroup([.. group.Select(item => item.Run)], group.First().Effective))];
    }

    private static EvalGroup ToGroup(IReadOnlyList<EvalRun> runs, Effective effective)
    {
        var names = string.Join(", ", runs.Select(run => run.Name));
        return new EvalGroup(
            runs,
            effective.Overrides,
            effective.Setup,
            effective.Model.ModelPath,
            effective.Model.TokenizerPath,
            [.. effective.Problems.Select(problem => $"Прогони {names}: {problem}")]);
    }

    private static Effective Resolve(EvalRun run, ChunkingOptions chunkingDefaults, EmbeddingModelOptions embeddingDefaults, ModelCatalogOptions catalog)
    {
        var chunking = new ChunkingOptions
        {
            Strategy = run.Chunking?.Strategy ?? chunkingDefaults.Strategy,
            ChunkSizeTokens = run.Chunking?.ChunkSizeTokens ?? chunkingDefaults.ChunkSizeTokens,
            OverlapPercent = run.Chunking?.OverlapPercent ?? chunkingDefaults.OverlapPercent,
        };

        var differsFromDefault = run.Model is not null
            && !string.Equals(run.Model, embeddingDefaults.ModelName, StringComparison.OrdinalIgnoreCase);
        var model = new EmbeddingModelOptions
        {
            ModelName = run.Model ?? embeddingDefaults.ModelName,

            // Шляхи, збережені для моделі за замовчуванням, не мають вказувати на файли не тієї моделі: каталог підставить свої.
            ModelPath = differsFromDefault ? string.Empty : embeddingDefaults.ModelPath,
            TokenizerPath = differsFromDefault ? string.Empty : embeddingDefaults.TokenizerPath,
            ModelSha256 = embeddingDefaults.ModelSha256,
            TokenizerSha256 = embeddingDefaults.TokenizerSha256,
            Dimensions = embeddingDefaults.Dimensions,
            MaxTokens = embeddingDefaults.MaxTokens,
            BatchSize = embeddingDefaults.BatchSize,
            QueryPrefix = embeddingDefaults.QueryPrefix,
            PassagePrefix = embeddingDefaults.PassagePrefix,
        };
        ServiceCollectionExtensions.ApplyCatalog(model, catalog);

        var problems = new List<string>();
        if (differsFromDefault && catalog.Find(run.Model) is null)
        {
            problems.Add(
                $"моделі «{run.Model}» немає в каталозі. Доступні: {string.Join(", ", catalog.Models.Select(m => m.Name))}.");
        }
        else if (chunking.ChunkSizeTokens > model.MaxTokens)
        {
            problems.Add(
                $"розмір фрагмента {chunking.ChunkSizeTokens} токенів більший за ліміт моделі {model.ModelName} " +
                $"({model.MaxTokens}); зменште chunkSizeTokens до ~{model.MaxTokens - (model.MaxTokens / 16)}.");
        }
        else if (chunking.ChunkSizeTokens <= ChunkSizeWithinModelLimitValidator.ReservedTokens(model))
        {
            problems.Add(
                $"розмір фрагмента {chunking.ChunkSizeTokens} токенів не більший за резерв під префікс і службові токени " +
                $"моделі {model.ModelName}.");
        }

        var setup = new EvalSetup(model.ModelName, chunking.Strategy, chunking.ChunkSizeTokens, chunking.OverlapPercent);
        return new Effective(setup, model, Overrides(setup, differsFromDefault), problems);
    }

    private static Dictionary<string, string?> Overrides(EvalSetup setup, bool modelDiffers)
    {
        var overrides = new Dictionary<string, string?>
        {
            [$"{ChunkingOptions.SectionName}:{nameof(ChunkingOptions.Strategy)}"] = setup.Strategy.ToString(),
            [$"{ChunkingOptions.SectionName}:{nameof(ChunkingOptions.ChunkSizeTokens)}"] =
                setup.ChunkSizeTokens.ToString(CultureInfo.InvariantCulture),
            [$"{ChunkingOptions.SectionName}:{nameof(ChunkingOptions.OverlapPercent)}"] =
                setup.OverlapPercent.ToString(CultureInfo.InvariantCulture),
        };

        if (modelDiffers)
        {
            overrides[$"{EmbeddingModelOptions.SectionName}:{nameof(EmbeddingModelOptions.ModelName)}"] = setup.Model;
            overrides[$"{EmbeddingModelOptions.SectionName}:{nameof(EmbeddingModelOptions.ModelPath)}"] = string.Empty;
            overrides[$"{EmbeddingModelOptions.SectionName}:{nameof(EmbeddingModelOptions.TokenizerPath)}"] = string.Empty;
        }

        return overrides;
    }

    private sealed record Effective(
        EvalSetup Setup,
        EmbeddingModelOptions Model,
        Dictionary<string, string?> Overrides,
        List<string> Problems);
}
