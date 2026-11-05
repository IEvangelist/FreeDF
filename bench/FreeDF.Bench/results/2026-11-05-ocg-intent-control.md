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
| **Interpret** | **AllOff** | **84.29 μs** | **3.721 μs** | **1.946 μs** | **8.1744** | **0.1703** | **140.19 KB** |
| **Interpret** | **AllOn**  | **85.56 μs** | **1.481 μs** | **0.658 μs** | **8.2237** | **0.1645** | **140.19 KB** |
| **Interpret** | **AnyOff** | **86.76 μs** | **4.130 μs** | **2.160 μs** | **8.2325** | **0.1680** | **140.19 KB** |
| **Interpret** | **AnyOn**  | **83.96 μs** | **0.569 μs** | **0.253 μs** | **8.3220** | **0.1698** | **140.19 KB** |
