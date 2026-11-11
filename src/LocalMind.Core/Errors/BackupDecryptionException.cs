namespace LocalMind.Core.Errors;

/// <summary>
/// Бекап не вдалося розшифрувати: неправильний пароль, пошкоджений або підмінений файл, невідомий формат.
/// Неправильний пароль і підміну навмисно не розрізняємо — GCM їх і не відрізняє.
/// </summary>
public class BackupDecryptionException : LocalMindException
{
    public const string DefaultMessage = "Не вдалося розшифрувати бекап: неправильний пароль або файл пошкоджено.";

    public BackupDecryptionException()
        : base(DefaultMessage)
    {
    }

    public BackupDecryptionException(string message)
        : base(message)
    {
    }

    public BackupDecryptionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
