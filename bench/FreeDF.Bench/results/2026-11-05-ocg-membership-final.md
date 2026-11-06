```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
Intel Core i7-14700K 3.40GHz, 1 CPU, 28 logical and 20 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3
  Job-TSMBOA : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3

IterationCount=8  IterationTime=500ms  LaunchCount=1
WarmupCount=3

```
| Method    | Policy | Mean     | Error     | StdDev   | Gen0   | Gen1   | Allocated |
|---------- |------- |---------:|----------:|---------:|-------:|-------:|----------:|
| **Interpret** | **AllOff** | **87.36 μs** |  **2.085 μs** | **1.091 μs** | **8.1909** | **0.1781** | **140.19 KB** |
| **Interpret** | **AllOn**  | **91.95 μs** | **10.452 μs** | **5.466 μs** | **8.1967** | **0.1708** | **140.19 KB** |
| **Interpret** | **AnyOff** | **89.87 μs** |  **4.471 μs** | **1.985 μs** | **8.2478** | **0.1833** | **140.19 KB** |
| **Interpret** | **AnyOn**  | **99.48 μs** |  **9.754 μs** | **5.102 μs** | **8.2237** | **0.1827** | **140.19 KB** |
