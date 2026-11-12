using LocalMind.Core;
using LocalMind.Search;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LocalMind.Cli;

/// <summary>Збирання DI-контейнера консольної утиліти.</summary>
internal static class CliServices
{
    /// <summary>Контейнер ядра й пошуку; логи — лише попередження й помилки.</summary>
    public static ServiceProvider Build(IConfiguration configuration) =>
        new ServiceCollection()
            .AddLogging(logging => logging
                .AddSimpleConsole(console => console.SingleLine = true)
                .SetMinimumLevel(LogLevel.Warning))
            .AddLocalMindCore(configuration)
            .AddLocalMindSearch(configuration)
            .BuildServiceProvider();
}
