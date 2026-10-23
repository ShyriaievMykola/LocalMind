namespace LocalMind.Core.Models;

/// <summary>Фрагмент документа — одиниця індексації та пошуку.</summary>
public sealed record Chunk
{
    /// <param name="id">Ідентифікатор фрагмента.</param>
    /// <param name="documentId">Документ, до якого належить фрагмент.</param>
    /// <param name="ordinal">Порядковий номер фрагмента в документі, починаючи з 0.</param>
    /// <param name="text">Текст фрагмента.</param>
    /// <param name="startOffset">Позиція початку фрагмента у тексті документа — для переходу до джерела.</param>
    /// <exception cref="ArgumentException">Порушено одне з обмежень аргументів.</exception>
    public Chunk(Guid id, Guid documentId, int ordinal, string text, int startOffset)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Ідентифікатор фрагмента не може бути порожнім.", nameof(id));
        }

        if (documentId == Guid.Empty)
        {
            throw new ArgumentException("Ідентифікатор документа не може бути порожнім.", nameof(documentId));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(ordinal);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentOutOfRangeException.ThrowIfNegative(startOffset);

        Id = id;
        DocumentId = documentId;
        Ordinal = ordinal;
        Text = text;
        StartOffset = startOffset;
    }

    /// <summary>Ідентифікатор фрагмента.</summary>
    public Guid Id { get; }

    /// <summary>Документ, до якого належить фрагмент.</summary>
    public Guid DocumentId { get; }

    /// <summary>Порядковий номер фрагмента в документі, починаючи з 0.</summary>
    public int Ordinal { get; }

    /// <summary>Текст фрагмента.</summary>
    public string Text { get; }

    /// <summary>Позиція початку фрагмента у тексті документа.</summary>
    public int StartOffset { get; }
}
