using System.CommandLine;
using LocalMind.Core.Ingestion;
using LocalMind.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace LocalMind.Cli.Commands;

/// <summary>Команда <c>index &lt;папка&gt;</c>: індексує файли папки у локальне сховище.</summary>
internal static class IndexCommand
{
    public static Command Create(IServiceProvider services)
    {
        var folder = new Argument<string>("папка") { Description = "Папка з нотатками" };
        folder.Validators.Add(result =>
        {
            var path = result.GetValue(folder);
            if (!Directory.Exists(path))
            {
                result.AddError($"Папку не знайдено: {path}");
            }
        });
        var command = new Command("index", "Проіндексувати папку з нотатками") { folder };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            await CliStartup.PrepareAsync(services, cancellationToken);

            var root = Path.GetFullPath(parseResult.GetRequiredValue(folder));
            var indexer = services.GetRequiredService<FolderIndexer>();
            var result = await indexer.IndexAsync(root, new ConsoleProgress(root), cancellationToken);

            Console.WriteLine();
            Console.WriteLine(
                $"Проіндексовано: {result.Indexed} (фрагментів: {result.Chunks}), без змін: {result.Unchanged}, " +
                $"без тексту: {result.WithoutText}, помилок: {result.Failed}, прибрано: {result.Removed}");
            return result.Failed > 0 ? ExitCodes.Error : ExitCodes.Success;
        });

        return command;
    }

    /// <summary>Друкує прогрес одразу, без маршалінгу в контекст, як робить <see cref="Progress{T}"/>.</summary>
    private sealed class ConsoleProgress(string root) : IProgress<IndexingProgress>
    {
        public void Report(IndexingProgress value)
        {
            if (value.CurrentFile is not null)
            {
                Console.WriteLine($"[{value.Processed + 1}/{value.Total}] {Path.GetRelativePath(root, value.CurrentFile)}");
            }
        }
    }
}
