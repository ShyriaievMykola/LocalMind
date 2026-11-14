# E4 на реальних embeddings (open-eval)

Доповнення до [e4-hnsw.md](e4-hnsw.md): ті самі індекси на справжніх векторах `multilingual-e5-small` (384 виміри), а не на синтетичних.

**Дані.** 12 781 вектор фрагментів підкорпусу `open-eval` (нарізка 256/15 %, база розмітки). Із них 200 випадкових векторів — запити (їх вилучено з індексу), решта 12 581 — корпус.
Еталон — повний перебір (`BruteForceIndex`); HNSW із параметрами за замовчуванням (M = 16, efConstruction = 200); recall@10 — частка точних десяти сусідів, яку знайшов HNSW; затримка —
середній час одного запиту, один потік. Заміри короткі (один прогін, один ноутбук, AMD Ryzen 7 7735HS).

| Векторів у індексі | Побудова HNSW, с | Повний перебір, мс | efSearch | Recall@10 | HNSW, мс |
|---:|---:|---:|---:|---:|---:|
| 1 000 | 0,5 | 0,06 | 20 | 0,995 | 0,06 |
| 1 000 | 0,5 | 0,06 | 100 | 1,000 | 0,10 |
| 5 000 | 1,7 | 0,40 | 20 | 0,996 | 0,08 |
| 5 000 | 1,7 | 0,40 | 100 | 1,000 | 0,26 |
| 12 581 | 6,5 | 1,45 | 20 | 0,998 | 0,10 |
| 12 581 | 6,5 | 1,45 | 50 | 1,000 | 0,19 |
| 12 581 | 6,5 | 1,45 | 100 | 1,000 | 0,31 |
| 12 581 | 6,5 | 1,45 | 400 | 1,000 | 0,98 |

**Висновок.** На справжніх embeddings граф поводиться як на кластеризованих даних E4, а не як на рівномірному шумі: recall@10 ≥ 0,995 уже при `efSearch = 20`, 1,000 при `efSearch ≥ 50`.
На 12,6 тис. векторів HNSW швидший за повний перебір у 4,7–7,6 раза (0,19–0,31 мс проти 1,45 мс при `efSearch` 50–100); на 1 000 векторів вигоди немає (0,06 мс в обох).

**Обмеження.** Це легкий випадок: запити — вектори фрагментів із того самого корпусу, а сусідні фрагменти перекриваються на 15 %, тож у кожного запиту є майже дублікати серед сусідів. Запити
користувача (короткі тексти, інший розподіл) складніші; їхній recall потрібно міряти окремо. Розмір 12,6 тис. вдесятеро менший за 100k, де в синтетичних даних HNSW давав виграш у десятки разів.

## Як повторити

Одноразовий консольний проєкт (посилається на `LocalMind.Search`, назва збірки `LocalMind.Benchmarks`, щоб бачити `HnswIndex.Search(query, k, efSearch)`); аргумент — шлях до бази, створеної
`localmind index` з моделлю e5-small:

```csharp
using System.Diagnostics;
using LocalMind.Search.Configuration;
using LocalMind.Search.Vectors;
using Microsoft.Data.Sqlite;

var db = args[0];
var vectors = new List<(Guid Id, float[] V)>();
using (var connection = new SqliteConnection($"Data Source={db};Mode=ReadOnly"))
{
    connection.Open();
    using var cmd = connection.CreateCommand();
    cmd.CommandText = "SELECT e.ChunkId, e.Vector FROM Embeddings e";
    using var reader = cmd.ExecuteReader();
    while (reader.Read())
    {
        var bytes = (byte[])reader[1];
        var v = new float[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, v, 0, bytes.Length);
        vectors.Add((Guid.Parse(reader.GetString(0)), v));
    }
}

var dim = vectors[0].V.Length;
var rnd = new Random(7);
var shuffled = vectors.OrderBy(_ => rnd.Next()).ToList();
var queries = shuffled.Take(200).ToList();
var corpus = shuffled.Skip(200).ToList();
Console.WriteLine($"vectors={vectors.Count} dim={dim} corpus={corpus.Count} queries={queries.Count}");

using var exact = new BruteForceIndex(dim);
foreach (var (id, v) in corpus) exact.Add(id, v);
var truth = queries.Select(q => exact.Search(q.V, 10).Select(r => r.ChunkId).ToHashSet()).ToArray();
var sw = Stopwatch.StartNew();
foreach (var q in queries) exact.Search(q.V, 10);
var bruteMs = sw.Elapsed.TotalMilliseconds / queries.Count;

var sizes = new[] { 1000, 5000, corpus.Count };
foreach (var n in sizes.Distinct())
{
    var sub = corpus.Take(n).ToList();
    using var ex = new BruteForceIndex(dim);
    foreach (var (id, v) in sub) ex.Add(id, v);
    var tr = queries.Select(q => ex.Search(q.V, 10).Select(r => r.ChunkId).ToHashSet()).ToArray();
    sw.Restart();
    foreach (var q in queries) ex.Search(q.V, 10);
    var bm = sw.Elapsed.TotalMilliseconds / queries.Count;

    var opts = new VectorIndexOptions();
    using var h = new HnswIndex(dim, opts);
    sw.Restart();
    foreach (var (id, v) in sub) h.Add(id, v);
    var build = sw.Elapsed.TotalSeconds;
    foreach (var ef in new[] { 20, 50, 100, 200, 400 })
    {
        double rec = 0;
        sw.Restart();
        for (var i = 0; i < queries.Count; i++)
            rec += h.Search(queries[i].V, 10, ef).Count(r => tr[i].Contains(r.ChunkId)) / 10.0;
        var ms = sw.Elapsed.TotalMilliseconds / queries.Count;
        Console.WriteLine($"n={n} build={build:0.0}s brute={bm:0.00}ms ef={ef} recall={rec / queries.Count:0.000} hnsw={ms:0.00}ms");
    }
}
```
