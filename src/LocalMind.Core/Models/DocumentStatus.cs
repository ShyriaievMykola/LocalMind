namespace LocalMind.Core.Models;

/// <summary>Результат індексації документа.</summary>
public enum DocumentStatus
{
    /// <summary>Документ проіндексовано, фрагменти доступні для пошуку.</summary>
    Indexed,

    /// <summary>Файл порожній.</summary>
    Empty,

    /// <summary>Файл не порожній, але тексту в ньому не знайдено (наприклад, сканований PDF).</summary>
    NoText,

    /// <summary>Під час індексації сталася помилка; причина — у <see cref="Document.Error"/>.</summary>
    Error,
}
