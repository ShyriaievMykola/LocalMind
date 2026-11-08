using LocalMind.Core.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LocalMind.Cli;

/// <summary>Підготовка перед виконанням команди; для <c>--help</c> не потрібна.</summary>
internal static class CliStartup
{
    /// <summary>Перевіряє налаштування і готує сховище.</summary>
    public static async Task PrepareAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        services.GetRequiredService<IStartupValidator>().Validate();
        await services.InitializeLocalMindStorageAsync(cancellationToken);
    }
}
