namespace LocalMind.Core.Models;

/// <summary>
/// Для чого обчислюється embedding. Частина моделей (наприклад, multilingual-e5)
/// вимагає різних префіксів для запиту і для фрагмента документа.
/// </summary>
public enum EmbeddingPurpose
{
    /// <summary>Текст запиту користувача.</summary>
    Query,

    /// <summary>Фрагмент документа, що індексується.</summary>
    Passage,
}
