using LocalMind.Core;
using LocalMind.Core.Configuration;
using LocalMind.Core.Diagnostics;
using LocalMind.Search;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LocalMind.Cli;

/// <summary>Збирання DI-контейнера консольної утиліти.</summary>
internal static class CliServices
{
    /// <summary>Ключ конфігурації теки журналів (<c>LocalMind__Logging__Directory</c>).</summary>
    public const string LogsDirectoryKey = "LocalMind:Logging:Directory";

    /// <summary>
    /// Тека журналів <c>cli-*.log</c> і звітів про аварії: <c>logs/</c> у теці даних користувача, поруч із базою,
    /// або <see cref="LogsDirectoryKey"/>.
    /// </summary>
    public static string LogsDirectory(IConfiguration configuration)
    {
        var configured = configuration[LogsDirectoryKey];
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Path.GetDirectoryName(StorageOptions.DefaultDatabasePath())!, "logs")
            : Path.GetFullPath(configured);
    }

    /// <summary>Контейнер ядра й пошуку; логи — лише попередження й помилки, у консоль і у файл.</summary>
    public static ServiceProvider Build(IConfiguration configuration) =>
        new ServiceCollection()
            .AddLogging(logging => logging
                .AddSimpleConsole(console => console.SingleLine = true)
                .SetMinimumLevel(LogLevel.Warning))
            .AddSingleton<ILoggerProvider>(_ => new FileLoggerProvider(LogsDirectory(configuration), "cli"))
            .AddLocalMindCore(configuration)
            .AddLocalMindSearch(configuration)
            .BuildServiceProvider();
}
