namespace LocalMind.Core.Errors;

/// <summary>
/// Файл не вдалося розібрати: він пошкоджений, зашифрований або має непідтримувану структуру.
/// Індексатор позначає такий документ статусом <see cref="Models.DocumentStatus.Error"/>
/// з повідомленням винятку як причиною.
/// </summary>
public class DocumentParseException : LocalMindException
{
    public DocumentParseException()
    {
    }

    public DocumentParseException(string message)
        : base(message)
    {
    }

    public DocumentParseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
