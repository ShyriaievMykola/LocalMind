using LocalMind.Core.Models;

namespace LocalMind.TestUtilities;

/// <summary>Фабрики коректних тестових об'єктів — щоб у тестах указувати лише те, що важливо.</summary>
public static class TestData
{
    /// <summary>Фіксований момент часу для детермінованих тестів.</summary>
    public static readonly DateTimeOffset Now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Коректний SHA-256 хеш; різні <paramref name="seed"/> дають різні хеші.</summary>
    public static string Hash(int seed = 0) =>
        Convert.ToHexStringLower(BitConverter.GetBytes(seed)).PadLeft(Core.Models.Document.ContentHashLength, '0');

    /// <summary>Документ зі статусом <see cref="DocumentStatus.Indexed"/>, якщо не вказано інше.</summary>
    public static Document Document(
        string path = "/notes/note.md",
        DocumentStatus status = DocumentStatus.Indexed,
        Guid? id = null,
        int hashSeed = 0) =>
        new(
            id ?? Guid.NewGuid(),
            path,
            Hash(hashSeed),
            status,
            Now,
            status == DocumentStatus.Error ? "Тестова помилка" : null);

    /// <summary>Фрагмент документа.</summary>
    public static Chunk Chunk(Guid documentId, int ordinal = 0, string text = "текст фрагмента", int startOffset = 0) =>
        new(Guid.NewGuid(), documentId, ordinal, text, startOffset);

    /// <summary>Фрагменти документа з послідовними номерами і зсувами.</summary>
    public static IReadOnlyList<Chunk> Chunks(Guid documentId, params string[] texts)
    {
        ArgumentNullException.ThrowIfNull(texts);

        var offset = 0;
        var chunks = new List<Chunk>(texts.Length);
        for (var i = 0; i < texts.Length; i++)
        {
            chunks.Add(Chunk(documentId, i, texts[i], offset));
            offset += texts[i].Length + 1;
        }

        return chunks;
    }
}
