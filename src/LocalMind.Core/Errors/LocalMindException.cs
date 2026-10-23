namespace LocalMind.Core.Errors;

/// <summary>
/// Базовий тип доменних винятків LocalMind. Повідомлення адресоване користувачу:
/// хости (App, Cli, Server) показують його без змін.
/// </summary>
public class LocalMindException : Exception
{
    public LocalMindException()
    {
    }

    public LocalMindException(string message)
        : base(message)
    {
    }

    public LocalMindException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
