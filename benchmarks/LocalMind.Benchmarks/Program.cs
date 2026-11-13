using BenchmarkDotNet.Running;
using LocalMind.Benchmarks;

// Запуск: dotnet run -c Release --project benchmarks/LocalMind.Benchmarks -- --filter "*"
// Без --filter програма запитає, які бенчмарки запускати.
// Recall HNSW для E4 (BenchmarkDotNet його не міряє): ... -- --recall [--out eval/results/e4-recall.csv]
if (args.Contains("--recall"))
{
    var position = Array.IndexOf(args, "--out");
    var output = position >= 0 && position + 1 < args.Length ? args[position + 1] : Path.Combine("eval", "results", "e4-recall.csv");
    return RecallReport.Run(output);
}

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
return 0;
