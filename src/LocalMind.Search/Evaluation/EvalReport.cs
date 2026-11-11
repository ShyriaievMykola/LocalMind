using LocalMind.Core.Models;

namespace LocalMind.Search.Evaluation;

/// <summary>Один прогін експерименту: які режими пошуку проганяти і з яким k.</summary>
/// <param name="Name">Унікальна назва прогону для звіту (наприклад, <c>e1-bm25</c>).</param>
/// <param name="Modes">Режими пошуку.</param>
/// <param name="K">Глибина метрик: Recall@k, MRR і nDCG@k.</param>
public sealed record EvalRun(string Name, IReadOnlyList<SearchMode> Modes, int K);

/// <summary>
/// Рядок звіту: метрики одного прогону й режиму для всіх запитів (<see cref="Type"/> — <see langword="null"/>)
/// або для одного типу запитів.
/// </summary>
/// <param name="Run">Назва прогону.</param>
/// <param name="Mode">Режим пошуку.</param>
/// <param name="Type">Тип запитів або <see langword="null"/> для всіх.</param>
/// <param name="Queries">Скільки запитів ураховано.</param>
/// <param name="K">Глибина метрик.</param>
/// <param name="Recall">Середній Recall@k.</param>
/// <param name="Mrr">Середній обернений ранг (MRR).</param>
/// <param name="Ndcg">Середній nDCG@k.</param>
/// <param name="LatencyP50Ms">Медіана часу пошуку, мс.</param>
/// <param name="LatencyP95Ms">95-й перцентиль часу пошуку, мс.</param>
public sealed record EvalRow(
    string Run,
    SearchMode Mode,
    QueryType? Type,
    int Queries,
    int K,
    double Recall,
    double Mrr,
    double Ndcg,
    double LatencyP50Ms,
    double LatencyP95Ms);

/// <summary>Результат одного запуску <c>eval</c>: заміри всього прогону та таблиця метрик.</summary>
/// <param name="Dataset">Назва датасету (ім'я теки).</param>
/// <param name="Documents">Скільки документів проіндексовано.</param>
/// <param name="Chunks">Скільки фрагментів створено.</param>
/// <param name="IndexingTime">Час індексації корпусу.</param>
/// <param name="DatabaseBytes">Розмір бази після індексації; 0, якщо шлях не передано.</param>
/// <param name="PeakMemoryBytes">
/// Пікова пам'ять процесу. На Windows і Linux — справжній пік working set; на macOS ОС його не віддає, тож це найбільше
/// із замірів working set і пам'яті GC після індексації та кожного режиму (оцінка знизу).
/// </param>
/// <param name="Rows">Рядки результатів.</param>
public sealed record EvalReport(
    string Dataset,
    int Documents,
    int Chunks,
    TimeSpan IndexingTime,
    long DatabaseBytes,
    long PeakMemoryBytes,
    IReadOnlyList<EvalRow> Rows);
