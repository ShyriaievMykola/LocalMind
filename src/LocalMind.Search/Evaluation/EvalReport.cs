using LocalMind.Core.Configuration;
using LocalMind.Core.Models;

namespace LocalMind.Search.Evaluation;

/// <summary>
/// Нарізка, яку прогін задає замість налаштувань за замовчуванням. Незаповнене поле береться з конфігурації.
/// </summary>
/// <param name="Strategy">Стратегія нарізки.</param>
/// <param name="ChunkSizeTokens">Розмір фрагмента в токенах.</param>
/// <param name="OverlapPercent">Перекриття сусідніх фрагментів, % від розміру фрагмента.</param>
public sealed record EvalChunking(ChunkingStrategy? Strategy = null, int? ChunkSizeTokens = null, int? OverlapPercent = null);

/// <summary>Фактичні параметри, з якими проіндексовано корпус: модель і нарізка. Потрапляють у CSV і Markdown.</summary>
/// <param name="Model">Назва embedding-моделі.</param>
/// <param name="Strategy">Стратегія нарізки.</param>
/// <param name="ChunkSizeTokens">Розмір фрагмента в токенах.</param>
/// <param name="OverlapPercent">Перекриття, %.</param>
public sealed record EvalSetup(string Model, ChunkingStrategy Strategy, int ChunkSizeTokens, int OverlapPercent);

/// <summary>Один прогін експерименту: які режими пошуку проганяти, з яким k, нарізкою й моделлю.</summary>
/// <param name="Name">Унікальна назва прогону для звіту (наприклад, <c>e1-bm25</c>).</param>
/// <param name="Modes">Режими пошуку.</param>
/// <param name="K">Глибина метрик: Recall@k, MRR і nDCG@k.</param>
/// <param name="Chunking">Нарізка прогону; <see langword="null"/> — з конфігурації за замовчуванням.</param>
/// <param name="Model">Назва моделі з каталогу (<c>LocalMind:ModelCatalog</c>); <see langword="null"/> — модель за замовчуванням.</param>
public sealed record EvalRun(
    string Name,
    IReadOnlyList<SearchMode> Modes,
    int K,
    EvalChunking? Chunking = null,
    string? Model = null);

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
/// <param name="Setup">Фактичні модель і нарізка цього корпусу; <see langword="null"/>, якщо не відомі.</param>
public sealed record EvalReport(
    string Dataset,
    int Documents,
    int Chunks,
    TimeSpan IndexingTime,
    long DatabaseBytes,
    long PeakMemoryBytes,
    IReadOnlyList<EvalRow> Rows,
    EvalSetup? Setup = null);
