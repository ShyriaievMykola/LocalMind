using Avalonia;
using LocalMind.App.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace LocalMind.App;

sealed class Program
{
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(10);

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        using var host = AppHost.CreateBuilder(args).Build();

        try
        {
            // Повертається одразу: міграції і завантаження векторів ідуть у фоні (CoreStartup).
            host.Start();
        }
        catch (OptionsValidationException exception)
        {
            host.Services.GetRequiredService<CoreStatus>().SetFailed($"Некоректні налаштування: {exception.Message}");
        }

        try
        {
            return BuildAvaloniaApp(host.Services).StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            // Поза UI-контекстом синхронізації, щоб очікування не заблокувало потік.
            // Після зупинки using звільняє сервіси, зокрема IndexingService.
            Task.Run(() => host.StopAsync(ShutdownTimeout)).GetAwaiter().GetResult();
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp() => Configure(AppBuilder.Configure<App>());

    private static AppBuilder BuildAvaloniaApp(IServiceProvider services) =>
        Configure(AppBuilder.Configure(() => new App { Services = services }));

    private static AppBuilder Configure(AppBuilder builder)
        => builder
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
