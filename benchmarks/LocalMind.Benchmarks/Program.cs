using BenchmarkDotNet.Running;
using LocalMind.Benchmarks;

// Запуск: dotnet run -c Release --project benchmarks/LocalMind.Benchmarks -- --filter "*"
// Без --filter програма запитає, які бенчмарки запускати.
// Recall HNSW для E4 (BenchmarkDotNet його не міряє): ... -- --recall [--out eval/results/e4-recall.csv]
// Те саме на справжніх векторах: ... -- --real <вектори.f32> [--out eval/results/e4-real.csv]
if (args.Contains("--real"))
{
    var source = Array.IndexOf(args, "--real");
    var target = Array.IndexOf(args, "--out");
    return RecallReport.RunReal(args[source + 1], target >= 0 && target + 1 < args.Length ? args[target + 1] : Path.Combine("eval", "results", "e4-real.csv"));
}

if (args.Contains("--recall"))
{
    var position = Array.IndexOf(args, "--out");
    var output = position >= 0 && position + 1 < args.Length ? args[position + 1] : Path.Combine("eval", "results", "e4-recall.csv");
    return RecallReport.Run(output);
}

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
return 0;
