using LocalMind.Core.Models;

namespace LocalMind.Core.Storage;

/// <summary>
/// CHECK-обмеження схеми SQLite: база сама відхиляє некоректні значення, навіть якщо їх запише код повз репозиторій.
/// Імена й вирази використовують конфігурації сутностей і міграція <c>AddCheckConstraints</c>.
/// </summary>
public static class StorageChecks
{
    public const string ChunkOrdinalName = "CK_Chunks_Ordinal";
    public const string ChunkOrdinal = "\"Ordinal\" >= 0";

    public const string ChunkStartOffsetName = "CK_Chunks_StartOffset";
    public const string ChunkStartOffset = "\"StartOffset\" >= 0";

    public const string DocumentStatusName = "CK_Documents_Status";

    /// <summary>Статус — один зі значень <see cref="DocumentStatus"/> (у базі він зберігається рядком).</summary>
    public static string DocumentStatus =>
        $"\"Status\" IN ({string.Join(", ", Enum.GetNames<DocumentStatus>().Select(name => $"'{name}'"))})";

    public const string EmbeddingVectorName = "CK_Embeddings_Vector";

    /// <summary>Вектор не порожній і складається з цілого числа float32 (по 4 байти).</summary>
    public const string EmbeddingVector = "length(\"Vector\") > 0 AND length(\"Vector\") % 4 = 0";

    public const string EmbeddingModelNameName = "CK_EmbeddingModels_Name";
    public const string EmbeddingModelName = "length(trim(\"Name\")) > 0";

    public const string SourceFolderPathName = "CK_SourceFolders_Path";
    public const string SourceFolderPath = "length(trim(\"Path\")) > 0";
}
