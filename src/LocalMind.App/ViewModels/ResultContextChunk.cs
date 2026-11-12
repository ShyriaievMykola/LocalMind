using LocalMind.Core.Models;

namespace LocalMind.App.ViewModels;

/// <summary>Повний фрагмент у панелі контексту; знайдений фрагмент виділяється окремо.</summary>
public sealed record ResultContextChunk(Chunk Chunk, bool IsMatch)
{
    public string Text => Chunk.Text;

    public string Title => IsMatch ? "Знайдений фрагмент" : $"Фрагмент {(long)Chunk.Ordinal + 1}";
}
