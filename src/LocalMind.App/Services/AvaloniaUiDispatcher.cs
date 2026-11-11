using Avalonia.Threading;

namespace LocalMind.App.Services;

/// <summary>Переносить дії в UI-потік Avalonia незалежно від того, в якому потоці створено ViewModel.</summary>
internal sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public void Post(Action action) => Dispatcher.UIThread.Post(action);
}