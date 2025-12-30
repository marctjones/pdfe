using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using PdfEditor.Benchmarks;

// Run all benchmarks
// Usage: dotnet run -c Release
// For quick test: dotnet run -c Release -- --job short
BenchmarkRunner.Run<RedactionBenchmarks>(
    DefaultConfig.Instance
        .WithOptions(ConfigOptions.JoinSummary));
