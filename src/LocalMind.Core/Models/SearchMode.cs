namespace LocalMind.Core.Models;

/// <summary>Режим пошуку.</summary>
public enum SearchMode
{
    /// <summary>Повнотекстовий пошук за ключовими словами (BM25).</summary>
    Keyword,

    /// <summary>Пошук за змістом — близькістю embeddings.</summary>
    Semantic,

    /// <summary>Keyword і Semantic, злиті методом Reciprocal Rank Fusion.</summary>
    Hybrid,
}
