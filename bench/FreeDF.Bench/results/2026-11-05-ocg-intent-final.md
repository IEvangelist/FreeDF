```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
Intel Core i7-14700K 3.40GHz, 1 CPU, 28 logical and 20 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3
  Job-TSMBOA : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3

IterationCount=8  IterationTime=500ms  LaunchCount=1
WarmupCount=3

```
| Method    | Policy | Mean     | Error    | StdDev   | Gen0   | Gen1   | Allocated |
|---------- |------- |---------:|---------:|---------:|-------:|-------:|----------:|
| **Interpret** | **AllOff** | **85.04 μs** | **2.506 μs** | **0.894 μs** | **8.2514** | **0.1756** | **140.19 KB** |
| **Interpret** | **AllOn**  | **85.94 μs** | **2.968 μs** | **1.318 μs** | **8.2192** | **0.1712** | **140.19 KB** |
| **Interpret** | **AnyOff** | **86.69 μs** | **2.534 μs** | **1.325 μs** | **8.1967** | **0.1708** | **140.19 KB** |
| **Interpret** | **AnyOn**  | **87.03 μs** | **3.760 μs** | **1.966 μs** | **8.3220** | **0.1698** | **140.19 KB** |
