using LocalMind.Core.Models;

namespace LocalMind.App.ViewModels;

/// <summary>Повний фрагмент у панелі контексту; знайдений фрагмент виділяється окремо.</summary>
public sealed record ResultContextChunk(Chunk Chunk, int MatchedOrdinal)
{
    public string Text => Chunk.Text;

    public bool IsMatch => Chunk.Ordinal == MatchedOrdinal;

    public string Title => Chunk.Ordinal.CompareTo(MatchedOrdinal) switch
    {
        < 0 => "Попередній фрагмент",
        0 => "Знайдений фрагмент",
        _ => "Наступний фрагмент",
    };
}
