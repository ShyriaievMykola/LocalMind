namespace LocalMind.App.Services;

/// <summary>
/// Спільний прапорець «потрібен перезапуск»: після відновлення з бекапу база замінена, а векторний індекс
/// і бібліотека в пам'яті лишаються від старої. До перезапуску пошук та індексація вимкнені, у шапці є банер.
/// </summary>
public sealed class AppRestartState
{
    /// <summary>Що показати користувачу, коли дія недоступна до перезапуску.</summary>
    public const string Message =
        "Потрібен перезапуск: бібліотеку відновлено з бекапу. Закрийте й знову відкрийте LocalMind — " +
        "до цього пошук та індексація вимкнені.";

    public bool IsRequired { get; private set; }

    /// <summary>Перезапуск став потрібним. Викликається з UI-потоку.</summary>
    public event EventHandler? Changed;

    public void Require()
    {
        if (IsRequired)
        {
            return;
        }

        IsRequired = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
