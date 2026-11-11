namespace LocalMind.Search.Evaluation;

/// <summary>Запит для оцінки пошуку.</summary>
/// <param name="Id">Унікальний ідентифікатор у межах датасету.</param>
/// <param name="Text">Текст запиту.</param>
/// <param name="Type">Тип запиту.</param>
public sealed record EvalQuery(string Id, string Text, QueryType Type);

/// <summary>
/// Датасет для оцінки пошуку: корпус документів, запити й розмітка. Релевантність задана на рівні документів:
/// ідентифікатори фрагментів нестабільні (новий <see cref="Guid"/> на кожну індексацію), а шлях документа відносно
/// <see cref="CorpusDirectory"/> не змінюється.
/// </summary>
/// <param name="CorpusDirectory">Тека з файлами корпусу.</param>
/// <param name="Queries">Запити в порядку з файла.</param>
/// <param name="Relevance">
/// Для кожного запиту (за id) — документи (шлях відносно корпусу, роздільник <c>/</c>) і їхня релевантність:
/// 1 — відповідає, 2 — відповідає добре, 3 — ідеальна відповідь.
/// </param>
public sealed record EvalDataset(
    string CorpusDirectory,
    IReadOnlyList<EvalQuery> Queries,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> Relevance);
