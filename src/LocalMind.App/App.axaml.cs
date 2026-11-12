using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LocalMind.App.Services;
using LocalMind.App.ViewModels;
using LocalMind.App.Views;
using Microsoft.Extensions.DependencyInjection;

namespace LocalMind.App;

public partial class App : Application
{
    /// <summary>Сервіси хоста; <see langword="null"/> лише у візуальному дизайнері.</summary>
    public IServiceProvider? Services { get; init; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && Services is not null)
        {
            GlobalExceptionHandler.Install(Services.GetRequiredService<UnhandledErrorReporter>());

            // Збережена тема — до показу вікна, щоб воно не блимнуло іншою темою.
            Services.GetRequiredService<IThemeService>().Apply(Services.GetRequiredService<IUserSettingsService>().Current.Theme);
            desktop.MainWindow = new MainWindow
            {
                DataContext = Services.GetRequiredService<MainViewModel>(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}