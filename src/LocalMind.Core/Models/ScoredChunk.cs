namespace LocalMind.Core.Models;

/// <summary>Ідентифікатор фрагмента з оцінкою — результат пошуку в одному з індексів.</summary>
/// <param name="ChunkId">Ідентифікатор фрагмента.</param>
/// <param name="Score">Оцінка релевантності: чим більша, тим краще; шкала залежить від індексу.</param>
public readonly record struct ScoredChunk(Guid ChunkId, double Score);
