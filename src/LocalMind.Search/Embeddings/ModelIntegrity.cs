using System.Security.Cryptography;
using LocalMind.Core.Errors;

namespace LocalMind.Search.Embeddings;

/// <summary>
/// Перевірка цілісності файлів моделі за SHA-256. Підмінена модель — це чужий код у вашому процесі
/// (ONNX Runtime виконує те, що описано в файлі) або мовчки хибні вектори, тому файл з іншим хешем не завантажується.
/// </summary>
internal static class ModelIntegrity
{
    /// <summary>
    /// Відкриває файл на читання з забороною запису й видалення іншими процесами, перевіряє хеш і повертає відкритий потік.
    /// Файл лишається заблокованим, доки потік не закрито, — викликач завантажує модель, поки тримає його,
    /// тож між перевіркою і завантаженням файл не підмінити. (На Linux і macOS блокування дорадчі.)
    /// </summary>
    /// <param name="path">Шлях до файла.</param>
    /// <param name="expectedSha256">Очікуваний хеш (hex); порожній — не перевіряти.</param>
    /// <param name="description">Що за файл, для повідомлення: «Файл моделі», «Файл токенізатора».</param>
    /// <exception cref="LocalMindException">Хеш не збігається.</exception>
    public static FileStream OpenVerified(string path, string? expectedSha256, string description)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            if (!string.IsNullOrEmpty(expectedSha256))
            {
                var actual = Convert.ToHexStringLower(SHA256.HashData(stream));
                if (!actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new LocalMindException(
                        $"{description} {path} не збігається з очікуваним SHA-256: " +
                        $"очікувався {expectedSha256.ToLowerInvariant()}, отримано {actual}. " +
                        "Файл підмінено або пошкоджено — завантажте його знову: scripts/download-models.");
                }

                stream.Position = 0;
            }

            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }
}
