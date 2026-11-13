using System.Globalization;
using LocalMind.Core.Configuration;
using LocalMind.Search.Configuration;

namespace LocalMind.Search.Evaluation;

/// <summary>
/// Одна комбінація нарізки й моделі, для якої корпус індексується окремо, і прогони, що її діляться.
/// </summary>
/// <param name="Runs">Прогони з однаковими нарізкою й моделлю, у порядку конфігу.</param>
/// <param name="ConfigOverrides">
/// Налаштування, які треба накласти на конфігурацію застосунку, щоб отримати цю комбінацію
/// (ключі у форматі <c>LocalMind:Section:Name</c>); порожній словник — конфігурація за замовчуванням.
/// </param>
public sealed record EvalGroup(IReadOnlyList<EvalRun> Runs, IReadOnlyDictionary<string, string?> ConfigOverrides);

/// <summary>
/// Розбиває прогони експерименту на групи за нарізкою й моделлю: кожна група — це окремий індекс у власній
/// тимчасовій базі, тож бібліотека користувача не змінюється, а результати різних комбінацій не змішуються.
/// </summary>
public static class EvalPlan
{
    /// <summary>Групує прогони; порядок груп і прогонів — як у конфігу.</summary>
    public static IReadOnlyList<EvalGroup> Build(IReadOnlyList<EvalRun> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);

        return [.. runs
            .GroupBy(Key)
            .Select(group => new EvalGroup([.. group], Overrides(group.First())))];
    }

    private static (string? Model, ChunkingStrategy? Strategy, int? Size, int? Overlap) Key(EvalRun run) =>
        (run.Model?.ToLowerInvariant(), run.Chunking?.Strategy, run.Chunking?.ChunkSizeTokens, run.Chunking?.OverlapPercent);

    private static Dictionary<string, string?> Overrides(EvalRun run)
    {
        var overrides = new Dictionary<string, string?>();
        if (run.Chunking is { } chunking)
        {
            if (chunking.Strategy is { } strategy)
            {
                overrides[$"{ChunkingOptions.SectionName}:{nameof(ChunkingOptions.Strategy)}"] = strategy.ToString();
            }

            if (chunking.ChunkSizeTokens is { } size)
            {
                overrides[$"{ChunkingOptions.SectionName}:{nameof(ChunkingOptions.ChunkSizeTokens)}"] =
                    size.ToString(CultureInfo.InvariantCulture);
            }

            if (chunking.OverlapPercent is { } overlap)
            {
                overrides[$"{ChunkingOptions.SectionName}:{nameof(ChunkingOptions.OverlapPercent)}"] =
                    overlap.ToString(CultureInfo.InvariantCulture);
            }
        }

        if (run.Model is { } model)
        {
            overrides[$"{EmbeddingModelOptions.SectionName}:{nameof(EmbeddingModelOptions.ModelName)}"] = model;

            // Шляхи, збережені для попередньої моделі, не мають вказувати на файли не тієї моделі: каталог підставить свої.
            overrides[$"{EmbeddingModelOptions.SectionName}:{nameof(EmbeddingModelOptions.ModelPath)}"] = string.Empty;
            overrides[$"{EmbeddingModelOptions.SectionName}:{nameof(EmbeddingModelOptions.TokenizerPath)}"] = string.Empty;
        }

        return overrides;
    }
}
