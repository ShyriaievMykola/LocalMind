using System.Text.Json;
using System.Text.Json.Serialization;
using LocalMind.Core.Configuration;
using LocalMind.Core.Errors;
using LocalMind.Core.Models;

namespace LocalMind.Search.Evaluation;

/// <summary>
/// Конфіг експерименту — JSON з кількома прогонами одного датасету:
/// <c>{"runs": [{"name": "bm25", "modes": ["keyword"], "k": 10}, {"name": "all", "modes": ["keyword", "semantic", "hybrid"]}]}</c>.
/// Поле <c>k</c> необов'язкове (за замовчуванням <see cref="DefaultK"/>).
/// Прогін може задати власну нарізку й модель з каталогу (E2 і E3):
/// <c>{"name": "e3-128", "modes": ["hybrid"], "chunking": {"strategy": "fixedSize", "chunkSizeTokens": 128, "overlapPercent": 15}, "model": "multilingual-e5-base"}</c>.
/// Усі поля <c>chunking</c> необов'язкові; без <c>chunking</c> і <c>model</c> береться конфігурація за замовчуванням.
/// Корпус індексується один раз для кожної комбінації нарізки й моделі (див. <see cref="EvalPlan"/>).
/// </summary>
public static class EvalExperiment
{
    /// <summary>Глибина метрик за замовчуванням: nDCG@10.</summary>
    public const int DefaultK = 10;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    /// <summary>Читає конфіг із файла.</summary>
    /// <exception cref="LocalMindException">Файла немає, JSON некоректний або прогони задано неправильно.</exception>
    public static IReadOnlyList<EvalRun> Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            throw new LocalMindException($"Конфіг експерименту не знайдено: {path}.");
        }

        ConfigFile? config;
        try
        {
            config = JsonSerializer.Deserialize<ConfigFile>(File.ReadAllText(path), JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new LocalMindException($"{path}: некоректний JSON ({ex.Message}).", ex);
        }

        if (config?.Runs is not { Count: > 0 })
        {
            throw new LocalMindException($"{path}: немає жодного прогону в runs.");
        }

        var runs = new List<EvalRun>();
        foreach (var run in config.Runs)
        {
            if (string.IsNullOrWhiteSpace(run.Name))
            {
                throw new LocalMindException($"{path}: прогін без name.");
            }

            if (run.Modes is not { Count: > 0 })
            {
                throw new LocalMindException($"{path}: прогін {run.Name} без modes (keyword, semantic, hybrid).");
            }

            var k = run.K ?? DefaultK;
            if (k <= 0)
            {
                throw new LocalMindException($"{path}: прогін {run.Name}: k має бути більше 0.");
            }

            var chunking = ParseChunking(path, run);
            if (run.Model is not null && string.IsNullOrWhiteSpace(run.Model))
            {
                throw new LocalMindException($"{path}: прогін {run.Name}: model порожня.");
            }

            runs.Add(new EvalRun(run.Name, [.. run.Modes.Distinct()], k, chunking, run.Model?.Trim()));
        }

        var duplicate = runs.GroupBy(r => r.Name, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new LocalMindException($"{path}: назва прогону {duplicate.Key} повторюється.");
        }

        return runs;
    }

    private static EvalChunking? ParseChunking(string path, RunEntry run)
    {
        var chunking = run.Chunking;
        if (chunking is null)
        {
            return null;
        }

        if (chunking.Strategy is { } strategy && !Enum.IsDefined(strategy))
        {
            throw new LocalMindException($"{path}: прогін {run.Name}: невідома стратегія нарізки.");
        }

        if (chunking.ChunkSizeTokens is <= 0)
        {
            throw new LocalMindException($"{path}: прогін {run.Name}: chunkSizeTokens має бути більше 0.");
        }

        if (chunking.OverlapPercent is < 0 or > ChunkingOptions.MaxOverlapPercent)
        {
            throw new LocalMindException(
                $"{path}: прогін {run.Name}: overlapPercent має бути від 0 до {ChunkingOptions.MaxOverlapPercent}.");
        }

        return chunking;
    }

    private sealed record ConfigFile(List<RunEntry>? Runs);

    private sealed record RunEntry(string? Name, List<SearchMode>? Modes, int? K, EvalChunking? Chunking, string? Model);
}
