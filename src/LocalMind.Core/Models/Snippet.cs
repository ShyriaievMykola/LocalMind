namespace LocalMind.Core.Models;

/// <summary>Діапазон підсвітки в тексті уривка.</summary>
/// <param name="Start">Позиція першого символу відносно початку <see cref="Snippet.Text"/>.</param>
/// <param name="Length">Довжина в символах, більше нуля.</param>
public readonly record struct HighlightRange(int Start, int Length)
{
    /// <summary>Позиція одразу після діапазону.</summary>
    public int End => Start + Length;
}

/// <summary>
/// Уривок фрагмента для картки результату: найрелевантніша частина тексту і слова запиту, які треба підсвітити.
/// UI лише малює: позиції вже пораховано відносно <see cref="Text"/>.
/// </summary>
public sealed record Snippet
{
    /// <param name="text">Текст уривка.</param>
    /// <param name="offset">Позиція уривка у тексті фрагмента (<see cref="Chunk.Text"/>).</param>
    /// <param name="highlights">Діапазони підсвітки: у межах тексту, за зростанням, без перетинів.</param>
    /// <param name="hasTextBefore">Перед уривком у фрагменті є ще текст — UI може показати «…».</param>
    /// <param name="hasTextAfter">Після уривка у фрагменті є ще текст.</param>
    /// <exception cref="ArgumentException">Порушено одне з обмежень аргументів.</exception>
    public Snippet(
        string text,
        int offset,
        IReadOnlyList<HighlightRange> highlights,
        bool hasTextBefore = false,
        bool hasTextAfter = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentNullException.ThrowIfNull(highlights);

        var previousEnd = 0;
        foreach (var range in highlights)
        {
            if (range.Length <= 0 || range.Start < previousEnd || range.End > text.Length)
            {
                throw new ArgumentException(
                    $"Діапазон підсвітки {range} виходить за межі уривка, порожній або перетинає попередній.",
                    nameof(highlights));
            }

            previousEnd = range.End;
        }

        Text = text;
        Offset = offset;
        Highlights = [.. highlights];
        HasTextBefore = hasTextBefore;
        HasTextAfter = hasTextAfter;
    }

    /// <summary>Текст уривка.</summary>
    public string Text { get; }

    /// <summary>Позиція уривка у тексті фрагмента; у документі — <c>Chunk.StartOffset + Offset</c>.</summary>
    public int Offset { get; }

    /// <summary>Діапазони підсвітки відносно <see cref="Text"/>, за зростанням; порожньо, якщо збігів немає.</summary>
    public IReadOnlyList<HighlightRange> Highlights { get; }

    /// <summary>Перед уривком у фрагменті є ще текст.</summary>
    public bool HasTextBefore { get; }

    /// <summary>Після уривка у фрагменті є ще текст.</summary>
    public bool HasTextAfter { get; }

    /// <summary>Увесь текст фрагмента без підсвітки.</summary>
    public static Snippet Whole(Chunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        return new Snippet(chunk.Text, 0, []);
    }
}
