```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
Intel Core i7-14700K 3.40GHz, 1 CPU, 28 logical and 20 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3
  Job-TSMBOA : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3

IterationCount=8  IterationTime=500ms  LaunchCount=1
WarmupCount=3

```
| Method    | Policy | Mean      | Error     | StdDev    | Gen0   | Gen1   | Allocated |
|---------- |------- |----------:|----------:|----------:|-------:|-------:|----------:|
| **Interpret** | **AllOff** |  **88.24 μs** |  **0.642 μs** |  **0.285 μs** | **8.2283** | **0.1751** | **140.19 KB** |
| **Interpret** | **AllOn**  | **103.84 μs** | **19.645 μs** | **10.275 μs** | **8.2187** | **0.1911** | **140.19 KB** |
| **Interpret** | **AnyOff** |  **92.88 μs** |  **4.653 μs** |  **2.066 μs** | **8.1967** | **0.2049** | **140.19 KB** |
| **Interpret** | **AnyOn**  |  **92.28 μs** |  **5.026 μs** |  **2.628 μs** | **8.1140** |      **-** | **140.19 KB** |
