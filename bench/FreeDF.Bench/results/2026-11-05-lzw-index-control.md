```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
Intel Core i7-14700K 3.40GHz, 1 CPU, 28 logical and 20 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3
  Job-TSMBOA : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3

IterationCount=8  IterationTime=500ms  LaunchCount=1
WarmupCount=3

```
| Method | Pattern  | Strict | Mean      | Error     | StdDev    | Gen0     | Gen1    | Allocated |
|------- |--------- |------- |----------:|----------:|----------:|---------:|--------:|----------:|
| **Decode** | **Literal**  | **False**  | **761.83 μs** | **12.091 μs** |  **5.369 μs** | **145.3125** | **67.1875** | **2456.1 KB** |
| **Decode** | **Literal**  | **True**   | **776.33 μs** | **26.474 μs** | **13.846 μs** | **144.8171** | **68.5976** | **2448.1 KB** |
| **Decode** | **Repeated** | **False**  |  **27.40 μs** |  **4.892 μs** |  **2.558 μs** |  **22.3808** |  **8.8937** | **378.02 KB** |
| **Decode** | **Repeated** | **True**   |  **26.69 μs** |  **5.637 μs** |  **2.948 μs** |  **21.9210** |  **8.7316** | **370.02 KB** |
