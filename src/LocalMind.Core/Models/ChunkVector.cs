namespace LocalMind.Core.Models;

/// <summary>Збережений embedding фрагмента — для завантаження у векторний індекс.</summary>
/// <param name="ChunkId">Ідентифікатор фрагмента.</param>
/// <param name="Vector">L2-нормалізований вектор фрагмента.</param>
public readonly record struct ChunkVector(Guid ChunkId, ReadOnlyMemory<float> Vector);
