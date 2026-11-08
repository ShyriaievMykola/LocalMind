using System.CommandLine;
using System.Text;
using LocalMind.Cli;
using LocalMind.Cli.Commands;
using LocalMind.Core;
using LocalMind.Core.Errors;
using LocalMind.Search;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

Console.OutputEncoding = Encoding.UTF8;

// Налаштування: appsettings.json поруч із програмою, змінні оточення LocalMind__Embedding__ModelPath тощо.
var configuration = new ConfigurationBuilder()
    .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: false)
    .AddEnvironmentVariables()
    .Build();

await using var services = new ServiceCollection()
    .AddLogging(logging => logging
        .AddSimpleConsole(console => console.SingleLine = true)
        .SetMinimumLevel(LogLevel.Warning))
    .AddLocalMindCore(configuration)
    .AddLocalMindSearch(configuration)
    .BuildServiceProvider();

var root = new RootCommand("LocalMind: семантичний пошук по особистих нотатках")
{
    IndexCommand.Create(services),
    SearchCommand.Create(services),
};

try
{
    return await root.Parse(args).InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });
}
catch (OperationCanceledException)
{
    await Console.Error.WriteLineAsync("Скасовано.");
    return ExitCodes.Canceled;
}
catch (Exception exception) when (exception is LocalMindException or OptionsValidationException or IOException or ArgumentException)
{
    await Console.Error.WriteLineAsync(exception.Message);
    return ExitCodes.Error;
}
