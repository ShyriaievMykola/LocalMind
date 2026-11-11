using System.CommandLine;
using System.Globalization;
using System.Text;
using LocalMind.Core.Abstractions;
using LocalMind.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace LocalMind.Cli.Commands;

/// <summary>Команда <c>search "запит"</c>: виводить найближчі за змістом фрагменти.</summary>
internal static class SearchCommand
{
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
                Console.WriteLine($"   {Preview(results[i].Snippet)}");
                Console.WriteLine();
            }

            return ExitCodes.Success;
        });

        return command;
    }

    /// <summary>Уривок одним рядком; слова запиту — у квадратних дужках.</summary>
    internal static string Preview(Snippet snippet)
    {
        var builder = new StringBuilder();
        if (snippet.HasTextBefore)
        {
            builder.Append('…');
        }

        var position = 0;
        foreach (var range in snippet.Highlights)
        {
            builder.Append(snippet.Text, position, range.Start - position)
                .Append('[')
                .Append(snippet.Text, range.Start, range.Length)
                .Append(']');
            position = range.End;
        }

        builder.Append(snippet.Text, position, snippet.Text.Length - position);
        if (snippet.HasTextAfter)
        {
            builder.Append('…');
        }

        return string.Join(' ', builder.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
