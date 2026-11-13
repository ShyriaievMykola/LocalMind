using BenchmarkDotNet.Running;

// Запуск: dotnet run -c Release --project benchmarks/LocalMind.Benchmarks -- --filter "*"
// Без --filter програма запитає, які бенчмарки запускати.
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
