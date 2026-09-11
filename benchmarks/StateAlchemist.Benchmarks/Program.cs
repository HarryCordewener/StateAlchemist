using BenchmarkDotNet.Running;

// dotnet run -c Release --project benchmarks/StateAlchemist.Benchmarks -- --filter '*'
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
