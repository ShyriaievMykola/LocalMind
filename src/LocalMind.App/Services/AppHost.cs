using LocalMind.App.ViewModels;
using LocalMind.Core;
using LocalMind.Core.Configuration;
using LocalMind.Search;
using LocalMind.Search.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LocalMind.App.Services;

/// <summary>Збирає Generic Host застосунку: налаштування, ядро, пошук, фоновий запуск і ViewModel-і.</summary>
public static class AppHost
{
    /// <summary>
    /// Налаштування — <c>appsettings.json</c> поруч із програмою, змінні оточення (<c>LocalMind__Storage__DatabasePath</c>)
    /// і аргументи командного рядка. Відносні шляхи рахуються від папки застосунку, а не від поточної.
    /// </summary>
    public static HostApplicationBuilder CreateBuilder(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ApplicationName = "LocalMind",
            ContentRootPath = AppContext.BaseDirectory,
        });

        // Помилки реєстрації видно одразу під час запуску, а не тоді, коли користувач відкриє сторінку.
        builder.ConfigureContainer(new DefaultServiceProviderFactory(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }));

        builder.Services
            .AddLocalMindCore(builder.Configuration)
            .AddLocalMindSearch(builder.Configuration);

        builder.Services.PostConfigure<EmbeddingModelOptions>(options =>
        {
            options.ModelPath = AppPaths.ResolveAppFile(options.ModelPath, AppContext.BaseDirectory);
            options.TokenizerPath = AppPaths.ResolveAppFile(options.TokenizerPath, AppContext.BaseDirectory);
        });
        builder.Services.PostConfigure<StorageOptions>(options =>
            options.DatabasePath = AppPaths.ResolveDatabase(options.DatabasePath));

        builder.Services.AddSingleton<CoreStatus>();
        builder.Services.AddHostedService<CoreStartup>();

        // ViewModel-і — Transient: кожне відкриття сторінки отримує новий екземпляр.
        builder.Services.AddTransient<MainViewModel>();

        return builder;
    }
}
