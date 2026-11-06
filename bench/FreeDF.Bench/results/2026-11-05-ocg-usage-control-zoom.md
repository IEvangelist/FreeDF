```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
Intel Core i7-14700K 3.40GHz, 1 CPU, 28 logical and 20 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3
  Job-TSMBOA : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3

IterationCount=8  IterationTime=500ms  LaunchCount=1
WarmupCount=3

```
| Method    | Zoom | Mean     | Error    | StdDev   | Allocated |
|---------- |----- |---------:|---------:|---------:|----------:|
| **IsVisible** | **1**    | **98.43 ns** | **2.961 ns** | **1.549 ns** |         **-** |
| **IsVisible** | **3**    | **97.16 ns** | **2.566 ns** | **1.342 ns** |         **-** |
