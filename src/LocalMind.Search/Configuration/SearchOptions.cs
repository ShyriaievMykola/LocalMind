using LocalMind.Core.Models;

namespace LocalMind.Search.Configuration;

/// <summary>Налаштування пошуку. Секція конфігурації — <see cref="SectionName"/>.</summary>
public sealed class SearchOptions
{
    /// <summary>Секція конфігурації.</summary>
    public const string SectionName = "LocalMind:Search";

    /// <summary>Кількість результатів, якщо в запиті не вказано інше.</summary>
    public int DefaultTopK { get; set; } = SearchQuery.DefaultTopK;
    
    /// <summary>Режим, обраний на сторінці пошуку за замовчуванням.</summary>
    public SearchMode DefaultMode { get; set; } = SearchMode.Hybrid;

    /// <summary>
    /// Скільки кандидатів брати з кожного індексу (keyword і semantic) перед злиттям у hybrid-режимі.
    /// Не менше за <see cref="DefaultTopK"/>.
    /// </summary>
    public int CandidatesPerIndex { get; set; } = 50;

    /// <summary>Константа k у формулі Reciprocal Rank Fusion: score = Σ 1 / (k + rank).</summary>
    public int RrfK { get; set; } = 60;
}
