using System.Net;

namespace LocalMind.Core.Errors;

/// <summary>
/// Помилка сервера бекапу. Повідомлення — для користувача (title з ProblemDetails сервера або наше).
/// <see cref="IsSessionExpired"/> означає, що треба увійти знову.
/// </summary>
public class BackupApiException : LocalMindException
{
    public BackupApiException()
    {
    }

    public BackupApiException(string message)
        : base(message)
    {
    }

    public BackupApiException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public BackupApiException(HttpStatusCode? statusCode, string message, Exception? innerException = null)
        : base(message, innerException!)
    {
        StatusCode = statusCode;
    }

    /// <summary>HTTP-статус відповіді; <see langword="null"/>, якщо сервер недоступний.</summary>
    public HttpStatusCode? StatusCode { get; }

    /// <summary>Сесія завершилась або її відкликано — потрібен новий вхід.</summary>
    public bool IsSessionExpired => StatusCode == HttpStatusCode.Unauthorized;
}
