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

            // Усі групи перевіряються до старту: помилка в останній не має коштувати результатів перших.
            var groups = EvalPlan.Build(runs, configuration);
            CheckBeforeStart(groups);

            var outputFolder = Path.GetFullPath(parseResult.GetRequiredValue(output));
            var runOrder = runs.Select(run => run.Name).ToList();
            var fileName = $"{Path.GetFileName(Path.TrimEndingDirectorySeparator(datasetPath))}-{DateTime.Now:yyyyMMdd-HHmmss}";

            await WarmUpAsync(configuration, groups[0], cancellationToken);

            var reports = new List<EvalReport>();
            foreach (var group in groups)
            {
                try
                {
                    reports.Add(await RunInTemporaryLibraryAsync(configuration, data, group, cancellationToken));
                }
                catch (LocalMindException exception)
                {
                    var saved = reports.Count > 0 ? " Результати попередніх комбінацій збережено." : string.Empty;
                    throw new LocalMindException($"Прогони {group.RunNames}: {exception.Message}{saved}", exception);
                }

                // Після кожної групи: якщо наступна впаде, результати вже на диску.
                Write(fileName, reports, runOrder, outputFolder);
            }

            var (csv, markdown) = Write(fileName, reports, runOrder, outputFolder);
            Console.WriteLine(EvalReportWriter.ToMarkdown(reports, runOrder));
            Console.WriteLine($"Збережено: {csv}");
            Console.WriteLine($"Збережено: {markdown}");
            return ExitCodes.Success;
        });

        return command;
    }

    /// <summary>Каталог, ліміт моделі й файли моделі: усе, що можна знати до старту, перевіряється для всіх груп одразу.</summary>
    private static void CheckBeforeStart(IReadOnlyList<EvalGroup> groups)
    {
        var problems = new List<string>();
        foreach (var group in groups)
        {
            problems.AddRange(group.Problems);
            if (group.RequiresModel)
            {
                foreach (var file in new[] { group.ModelPath, group.TokenizerPath }.Where(path => !File.Exists(path)))
                {
                    problems.Add(
                        $"Прогони {group.RunNames}: файл моделі {group.Setup.Model} не знайдено: {file}. " +
                        $"Запустіть scripts/download-models {group.Setup.Model}.");
                }
            }
        }

        if (problems.Count > 0)
        {
            throw new LocalMindException(string.Join(Environment.NewLine, problems));
        }
    }

    /// <summary>
    /// Прогрів перед вимірюванням: JIT, SQLite і кеш файлової системи. Інакше перша комбінація «холодна» й
    /// її час індексації не порівнянний з наступними. Індексується крихітний корпус у тимчасовій базі, результат відкидається.
    /// </summary>
    private static async Task WarmUpAsync(IConfiguration configuration, EvalGroup group, CancellationToken cancellationToken)
    {
        var temp = Directory.CreateTempSubdirectory("localmind-eval-warmup-").FullName;
        try
        {
            var corpus = Directory.CreateDirectory(Path.Combine(temp, "corpus")).FullName;
            await File.WriteAllTextAsync(
                Path.Combine(corpus, "warmup.txt"),
                "Прогрів перед вимірюванням: кілька речень про каву, чай і базу даних SQLite.\n\nWarm-up text about search.",
                cancellationToken);

            var isolated = new ConfigurationBuilder()
                .AddConfiguration(configuration)
                .AddInMemoryCollection(group.ConfigOverrides)
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [$"{StorageOptions.SectionName}:{nameof(StorageOptions.DatabasePath)}"] = Path.Combine(temp, "warmup.db"),
                })
                .Build();

            await using var services = CliServices.Build(isolated);
            await CliStartup.PrepareAsync(services, cancellationToken);
            await services.GetRequiredService<FolderIndexer>().IndexAsync(corpus, cancellationToken: cancellationToken);

            var search = services.GetRequiredService<ISearchService>();
            await search.SearchAsync(new SearchQuery("прогрів", SearchMode.Keyword, 5), cancellationToken);
            if (group.RequiresModel)
            {
                await search.SearchAsync(new SearchQuery("прогрів", SearchMode.Hybrid, 5), cancellationToken);
            }
        }
        catch (OptionsValidationException exception)
        {
            throw new LocalMindException($"Прогони {group.RunNames}: {exception.Message}", exception);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDelete(temp);
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
                // Назви прогонів додає виклик зовні, один раз.
                throw new LocalMindException(exception.Message, exception);
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

    private static (string Csv, string Markdown) Write(
        string name,
        IReadOnlyList<EvalReport> reports,
        IReadOnlyList<string> runOrder,
        string folder)
    {
        Directory.CreateDirectory(folder);
        var csv = Path.Combine(folder, name + ".csv");
        var markdown = Path.Combine(folder, name + ".md");
        File.WriteAllText(csv, EvalReportWriter.ToCsv(reports, runOrder));
        File.WriteAllText(markdown, EvalReportWriter.ToMarkdown(reports, runOrder));
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
