using Microsoft.Extensions.Configuration;
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
    /// <param name="args">Аргументи командного рядка.</param>
    /// <param name="userSettingsPath">
    /// Файл налаштувань користувача; за замовчуванням <see cref="AppPaths.UserSettingsPath"/>. Тести передають власний.
    /// </param>
    public static HostApplicationBuilder CreateBuilder(string[] args, string? userSettingsPath = null)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ApplicationName = "LocalMind",
            ContentRootPath = AppContext.BaseDirectory,
        });

        // Налаштування зі сторінки «Налаштування» — поверх appsettings.json; файлу може ще не бути.
        var settingsFile = new UserSettingsFile(userSettingsPath ?? AppPaths.UserSettingsPath());
        builder.Configuration.AddJsonFile(settingsFile.Path, optional: true, reloadOnChange: false);

        // Помилки реєстрації видно одразу під час запуску, а не тоді, коли користувач відкриє сторінку.
        builder.ConfigureContainer(new DefaultServiceProviderFactory(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }));

        builder.Services
            .AddLocalMindCore(builder.Configuration)
            .AddLocalMindSearch(builder.Configuration)
            .AddLocalMindBackup(builder.Configuration);

        builder.Services.PostConfigure<EmbeddingModelOptions>(options =>
        {
            options.ModelPath = AppPaths.ResolveAppFile(options.ModelPath, AppContext.BaseDirectory, AppPaths.UserDataDirectory());
            options.TokenizerPath = AppPaths.ResolveAppFile(options.TokenizerPath, AppContext.BaseDirectory, AppPaths.UserDataDirectory());
        });
        builder.Services.PostConfigure<StorageOptions>(options =>
            options.DatabasePath = AppPaths.ResolveDatabase(options.DatabasePath));

        builder.Services.AddSingleton<CoreStatus>();
        builder.Services.AddSingleton<IFolderPicker, FolderPicker>();
        builder.Services.AddSingleton<IUiDispatcher, AvaloniaUiDispatcher>();
        builder.Services.AddOptions<AppOptions>().Bind(builder.Configuration.GetSection(AppOptions.SectionName));
        builder.Services.AddSingleton(settingsFile);
        builder.Services.AddSingleton<IUserSettingsService, UserSettingsService>();
        // Історія поруч із settings.json: тести, що передають власний файл налаштувань, не чіпають справжню історію.
        builder.Services.AddSingleton(new SearchHistoryFile(
            Path.Combine(Path.GetDirectoryName(settingsFile.Path)!, "search-history.json")));
        builder.Services.AddSingleton<ISearchHistory, SearchHistoryStore>();
        builder.Services.AddSingleton<IThemeService, ThemeService>();
        builder.Services.AddSingleton<IBackupServerSettings, BackupServerSettings>();
        builder.Services.AddTransient<IAccountClient, BackupAccountClient>();
        builder.Services.AddSingleton<UnhandledErrorReporter>();
        builder.Services.AddHostedService<CoreStartup>();
        builder.Services.AddSingleton<IFileLauncher, SystemFileLauncher>();
        builder.Services.AddSingleton<ResultCardFactory>();

        // Корінь вікна живе весь час роботи застосунку. Сторінки — Transient (див. docs/architecture.md, 3.5).
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddTransient<SearchViewModel>();
        builder.Services.AddTransient<LibraryViewModel>();
        builder.Services.AddTransient<AccountViewModel>();
        builder.Services.AddTransient<SettingsViewModel>();
        builder.Services.AddTransient<IndexingProgressViewModel>();

        return builder;
    }
}
