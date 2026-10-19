using BenchmarkDotNet.Running;
using Wyrd.Benchmarks;

if (args.Contains("--gate", StringComparer.Ordinal) || args.Contains("--write-baseline", StringComparer.Ordinal))
{
    return await AllocationGate.RunAsync(args);
}

await ShopBenchmarks.CalibrateAsync();
BenchmarkSwitcher.FromAssembly(typeof(ShopBenchmarks).Assembly).Run(args);
return 0;
