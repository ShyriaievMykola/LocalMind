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
    /// Читає файл у пам'ять, перевіряє хеш <b>цих самих байтів</b> і повертає їх: далі модель створюється з них,
    /// а не з файла за шляхом. Так між перевіркою й завантаженням нічого не підмінити, навіть де блокування файлів
    /// лише дорадчі (Linux, macOS). Байти потрібні лише на час створення сесії — викликач не має їх зберігати.
    /// </summary>
    /// <param name="path">Шлях до файла.</param>
    /// <param name="expectedSha256">Очікуваний хеш (hex); порожній — не перевіряти.</param>
    /// <param name="description">Що за файл, для повідомлення: «Файл моделі», «Файл токенізатора».</param>
    /// <exception cref="LocalMindException">Хеш не збігається або файл завеликий для читання в пам'ять.</exception>
    /// <exception cref="IOException">Файл зайнятий іншим процесом (наприклад, його саме завантажують) або недоступний.</exception>
    public static byte[] ReadVerified(string path, string? expectedSha256, string description)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > Array.MaxLength)
        {
            throw new LocalMindException($"{description} {path} завеликий: {stream.Length} байт.");
        }

        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);

        if (!string.IsNullOrEmpty(expectedSha256))
        {
            var actual = Convert.ToHexStringLower(SHA256.HashData(bytes));
            if (!actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new LocalMindException(
                    $"{description} {path} не збігається з очікуваним SHA-256: " +
                    $"очікувався {expectedSha256.ToLowerInvariant()}, отримано {actual}. " +
                    "Файл підмінено або пошкоджено — завантажте його знову: scripts/download-models.");
            }
        }

        return bytes;
    }
}
