using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using LocalMind.App.Services;

namespace LocalMind.App.ViewModels;

/// <summary>Корінь вікна. Поки показує лише стан запуску ядра; навігацію і сторінки додасть команда.</summary>
public partial class MainViewModel : ViewModelBase
{
    private readonly CoreStatus _status;

    public MainViewModel(CoreStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        _status = status;
        _status.Changed += (_, _) => Dispatcher.UIThread.Post(UpdateStatus);
        UpdateStatus();
    }

    [ObservableProperty]
    public partial string StatusText { get; private set; } = string.Empty;

    private void UpdateStatus() =>
        StatusText = _status.Stage switch
        {
            CoreStage.Starting => "Запуск…",
            CoreStage.Failed => _status.Error ?? "Помилка запуску.",
            _ when !_status.IsModelAvailable => $"Готово, але без моделі. {_status.ModelError}",
            _ => "Готово.",
        };
}
