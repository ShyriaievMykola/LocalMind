using System.CommandLine;
using LocalMind.Core.Abstractions;
using LocalMind.Core.Configuration;
using LocalMind.Core.Errors;
using LocalMind.Core.Ingestion;
using LocalMind.Core.Models;
using LocalMind.Search.Configuration;
using LocalMind.Search.Evaluation;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LocalMind.Cli.Commands;

/// <summary>
/// Команда <c>eval --dataset &lt;тека&gt;</c>: індексує корпус датасету в тимчасову базу, проганяє запити
/// й записує CSV та Markdown з метриками й замірами в <c>eval/results/</c>. Бібліотека користувача не чіпається.
/// Прогони з різною нарізкою чи моделлю (<c>--config</c>, E2 і E3) індексують корпус окремо, кожен у своїй тимчасовій базі.
/// </summary>
internal static class EvalCommand
{
    public static Command Create(IConfiguration configuration)
    {
        var dataset = new Option<string>("--dataset")
        {
            Description = "Тека датасету: corpus/, queries.jsonl, qrels.jsonl",
            Required = true,
        };
        var modes = new Option<SearchMode[]>("--mode")
        {
            Description = "Режими: keyword, semantic, hybrid (можна кілька); без параметра — усі три",
            AllowMultipleArgumentsPerToken = true,
            DefaultValueFactory = _ => [SearchMode.Keyword, SearchMode.Semantic, SearchMode.Hybrid],
        };
        var k = new Option<int>("--k")
        {
            Description = "Глибина метрик: Recall@k, MRR, nDCG@k",
            DefaultValueFactory = _ => EvalExperiment.DefaultK,
        };
        var experiment = new Option<string?>("--config")
        {
            Description = "JSON з кількома прогонами (name, modes, k, chunking, model); замінює --mode і --k",
        };
        var output = new Option<string>("--output")
        {
            Description = "Куди записати результати",
            DefaultValueFactory = _ => Path.Combine("eval", "results"),
        };
        k.Validators.Add(result =>
        {
            if (result.GetValue(k) <= 0)
            {
                result.AddError("--k має бути більше 0.");
            }
        });

        var command = new Command("eval", "Оцінити якість і швидкість пошуку на датасеті") { dataset, modes, k, experiment, output };
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var datasetPath = Path.GetFullPath(parseResult.GetRequiredValue(dataset));
            var data = EvalDatasetLoader.Load(datasetPath);
            var configPath = parseResult.GetValue(experiment);
            IReadOnlyList<EvalRun> runs = configPath is null
                ? [new EvalRun("cli", [.. parseResult.GetRequiredValue(modes).Distinct()], parseResult.GetValue(k))]
                : EvalExperiment.Load(configPath);

            var catalog = configuration.GetSection(ModelCatalogOptions.SectionName).Get<ModelCatalogOptions>() ?? new ModelCatalogOptions();
            CheckModelsInCatalog(runs, catalog);

            var reports = new List<EvalReport>();
            foreach (var group in EvalPlan.Build(runs))
            {
                try
                {
                    reports.Add(await RunInTemporaryLibraryAsync(configuration, data, group, cancellationToken));
                }
                catch (LocalMindException exception) when (group.Runs.Any(run => run.Model is not null))
                {
                    var names = string.Join(", ", group.Runs.Select(run => run.Name));
                    var models = string.Join(", ", group.Runs.Select(run => run.Model).Distinct(StringComparer.OrdinalIgnoreCase));
                    throw new LocalMindException(
                        $"Прогони {names} (модель {models}): {exception.Message}", exception);
                }
            }

            var (csv, markdown) = Write(reports[0].Dataset, reports, Path.GetFullPath(parseResult.GetRequiredValue(output)));
            Console.WriteLine(EvalReportWriter.ToMarkdown(reports));
            Console.WriteLine($"Збережено: {csv}");
            Console.WriteLine($"Збережено: {markdown}");
            return ExitCodes.Success;
        });

        return command;
    }

    private static void CheckModelsInCatalog(IReadOnlyList<EvalRun> runs, ModelCatalogOptions catalog)
    {
        foreach (var run in runs.Where(run => run.Model is not null))
        {
            if (catalog.Find(run.Model) is null)
            {
                throw new LocalMindException(
                    $"Прогін {run.Name}: моделі «{run.Model}» немає в каталозі. Доступні: " +
                    $"{string.Join(", ", catalog.Models.Select(m => m.Name))}.");
            }
        }
    }

    private static async Task<EvalReport> RunInTemporaryLibraryAsync(
        IConfiguration configuration,
        EvalDataset dataset,
        EvalGroup group,
        CancellationToken cancellationToken)
    {
        var temp = Directory.CreateTempSubdirectory("localmind-eval-").FullName;
        var databasePath = Path.Combine(temp, "eval.db");
        try
        {
            var isolated = new ConfigurationBuilder()
                .AddConfiguration(configuration)
                .AddInMemoryCollection(group.ConfigOverrides)
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [$"{StorageOptions.SectionName}:{nameof(StorageOptions.DatabasePath)}"] = databasePath,
                })
                .Build();

            await using var services = CliServices.Build(isolated);
            try
            {
                await CliStartup.PrepareAsync(services, cancellationToken);
            }
            catch (OptionsValidationException exception)
            {
                var names = string.Join(", ", group.Runs.Select(run => run.Name));
                throw new LocalMindException($"Прогони {names}: {exception.Message}", exception);
            }

            // Hybrid без моделі тихо деградує до keyword, а для експерименту це було б хибним результатом.
            if (group.Runs.Any(run => run.Modes.Any(mode => mode != SearchMode.Keyword)))
            {
                await services.GetRequiredService<IEmbedder>()
                    .EmbedAsync(["перевірка моделі"], EmbeddingPurpose.Query, cancellationToken);
            }

            var report = await EvalRunner.RunAsync(
                dataset,
                services.GetRequiredService<FolderIndexer>(),
                services.GetRequiredService<ISearchService>(),
                group.Runs,
                databasePath,
                cancellationToken);

            var chunking = services.GetRequiredService<IOptions<ChunkingOptions>>().Value;
            var setup = new EvalSetup(
                services.GetRequiredService<IOptions<EmbeddingModelOptions>>().Value.ModelName,
                chunking.Strategy,
                chunking.ChunkSizeTokens,
                chunking.OverlapPercent);
            return report with { Setup = setup };
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDelete(temp);
        }
    }

    private static (string Csv, string Markdown) Write(string dataset, IReadOnlyList<EvalReport> reports, string folder)
    {
        Directory.CreateDirectory(folder);
        var name = $"{dataset}-{DateTime.Now:yyyyMMdd-HHmmss}";
        var csv = Path.Combine(folder, name + ".csv");
        var markdown = Path.Combine(folder, name + ".md");
        File.WriteAllText(csv, EvalReportWriter.ToCsv(reports));
        File.WriteAllText(markdown, EvalReportWriter.ToMarkdown(reports));
        return (csv, markdown);
    }

    /// <summary>Тимчасова тека — наша власна; якщо файл ще зайнятий, її прибере ОС.</summary>
    private static void TryDelete(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
