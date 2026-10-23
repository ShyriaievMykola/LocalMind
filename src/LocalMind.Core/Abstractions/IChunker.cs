using LocalMind.Core.Models;

namespace LocalMind.Core.Abstractions;

/// <summary>Ріже текст документа на фрагменти. Стратегія нарізки — предмет експерименту E3.</summary>
public interface IChunker
{
    /// <summary>Нарізає текст на фрагменти.</summary>
    /// <param name="documentId">Документ, якому належатимуть фрагменти.</param>
    /// <param name="text">Текст документа.</param>
    /// <returns>
    /// Фрагменти в порядку появи в тексті: <see cref="Chunk.Ordinal"/> іде від 0 без пропусків,
    /// <see cref="Chunk.StartOffset"/> вказує на початок фрагмента в <paramref name="text"/>.
    /// Для порожнього тексту або тексту лише з пробілів — порожній список.
    /// </returns>
    IReadOnlyList<Chunk> Split(Guid documentId, string text);
}
