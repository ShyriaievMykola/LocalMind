using System.CommandLine;
using System.Globalization;
using LocalMind.Core.Abstractions;
using LocalMind.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace LocalMind.Cli.Commands;

/// <summary>Команда <c>search "запит"</c>: виводить найближчі за змістом фрагменти.</summary>
internal static class SearchCommand
{
    private const int MaxPreviewLength = 300;

    public static Command Create(IServiceProvider services)
    {
        var text = new Argument<string>("запит") { Description = "Текст запиту" };
        var topK = new Option<int>("--k")
        {
            Description = "Скільки результатів показати",
            DefaultValueFactory = _ => SearchQuery.DefaultTopK,
        };
        text.Validators.Add(result =>
        {
            if (string.IsNullOrWhiteSpace(result.GetValue(text)))
            {
                result.AddError("Запит не може бути порожнім.");
            }
        });
        topK.Validators.Add(result =>
        {
            if (result.GetValue(topK) <= 0)
            {
                result.AddError("--k має бути більше 0.");
            }
        });
        var command = new Command("search", "Знайти нотатки за змістом") { text, topK };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            await CliStartup.PrepareAsync(services, cancellationToken);

            var query = new SearchQuery(
                parseResult.GetRequiredValue(text),
                SearchMode.Semantic,
                parseResult.GetValue(topK));

            var results = await services.GetRequiredService<ISearchService>().SearchAsync(query, cancellationToken);
            if (results.Count == 0)
            {
                Console.WriteLine("Нічого не знайдено. Спочатку проіндексуйте папку: localmind index <папка>");
                return ExitCodes.Success;
            }

            for (var i = 0; i < results.Count; i++)
            {
                var score = results[i].Score.ToString("0.000", CultureInfo.InvariantCulture);
                Console.WriteLine($"{i + 1}. [{score}] {results[i].DocumentPath}");
                Console.WriteLine($"   {Preview(results[i].Chunk.Text)}");
                Console.WriteLine();
            }

            return ExitCodes.Success;
        });

        return command;
    }

    private static string Preview(string text)
    {
        var line = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return line.Length <= MaxPreviewLength ? line : string.Concat(line.AsSpan(0, MaxPreviewLength), "…");
    }
}
