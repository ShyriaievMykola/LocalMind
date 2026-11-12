using System.CommandLine;
using LocalMind.Core.Abstractions;
using LocalMind.Core.Configuration;
using LocalMind.Core.Ingestion;
using LocalMind.Core.Models;
using LocalMind.Search.Evaluation;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LocalMind.Cli.Commands;

/// <summary>
/// Команда <c>eval --dataset &lt;тека&gt;</c>: індексує корпус датасету в тимчасову базу, проганяє запити
/// й записує CSV та Markdown з метриками й замірами в <c>eval/results/</c>. Бібліотека користувача не чіпається.
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
            Description = "JSON з кількома прогонами (name, modes, k); замінює --mode і --k",
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

            var report = await RunInTemporaryLibraryAsync(configuration, data, runs, cancellationToken);

            var (csv, markdown) = Write(report, Path.GetFullPath(parseResult.GetRequiredValue(output)));
            Console.WriteLine(EvalReportWriter.ToMarkdown(report));
            Console.WriteLine($"Збережено: {csv}");
            Console.WriteLine($"Збережено: {markdown}");
            return ExitCodes.Success;
        });

        return command;
    }

    private static async Task<EvalReport> RunInTemporaryLibraryAsync(
        IConfiguration configuration,
        EvalDataset dataset,
        IReadOnlyList<EvalRun> runs,
        CancellationToken cancellationToken)
    {
        var temp = Directory.CreateTempSubdirectory("localmind-eval-").FullName;
        var databasePath = Path.Combine(temp, "eval.db");
        try
        {
            var isolated = new ConfigurationBuilder()
                .AddConfiguration(configuration)
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [$"{StorageOptions.SectionName}:{nameof(StorageOptions.DatabasePath)}"] = databasePath,
                })
                .Build();

            await using var services = CliServices.Build(isolated);
            await CliStartup.PrepareAsync(services, cancellationToken);

            // Hybrid без моделі тихо деградує до keyword, а для експерименту це було б хибним результатом.
            if (runs.Any(run => run.Modes.Any(mode => mode != SearchMode.Keyword)))
            {
                await services.GetRequiredService<IEmbedder>()
                    .EmbedAsync(["перевірка моделі"], EmbeddingPurpose.Query, cancellationToken);
            }

            return await EvalRunner.RunAsync(
                dataset,
                services.GetRequiredService<FolderIndexer>(),
                services.GetRequiredService<ISearchService>(),
                runs,
                databasePath,
                cancellationToken);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDelete(temp);
        }
    }

    private static (string Csv, string Markdown) Write(EvalReport report, string folder)
    {
        Directory.CreateDirectory(folder);
        var name = $"{report.Dataset}-{DateTime.Now:yyyyMMdd-HHmmss}";
        var csv = Path.Combine(folder, name + ".csv");
        var markdown = Path.Combine(folder, name + ".md");
        File.WriteAllText(csv, EvalReportWriter.ToCsv(report));
        File.WriteAllText(markdown, EvalReportWriter.ToMarkdown(report));
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
